using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13194 (review PR #510, MENOR-3) — «Enviar al OT» evalúa la firma ANTES de consultar el RUNT y de
/// persistir los checks: sin firma notifica a cada parte, responde <c>firma_pendiente</c> y no deja ningún
/// efecto de SOAT (ni consulta, ni field_values, ni guardado, ni transición).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var (r, error, _) = await new EnviarAlOtHandler(repo, lifecycle, soat, policy,
///     firmaGate: gate, firmaNotifier: notifier, ultimoBloqueo: bloqueo).HandleAsync(id, tenant, user, new(), ct);
/// // error == "firma_pendiente"; bloqueo.PartesSinFirma == [comprador (enviada)]
/// </code>
/// </remarks>
public sealed class EnviarAlOtFirmaPrimeroTests
{
    private const string ProviderKey = "runt-test";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITramiteLifecycleService _lifecycle = Substitute.For<ITramiteLifecycleService>();
    private readonly ICatalogRepository _catalog = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly IConsultationProvider _provider = Substitute.For<IConsultationProvider>();
    private readonly ISoatRuntValidationPolicy _policy = Substitute.For<ISoatRuntValidationPolicy>();
    private readonly ITramiteFirmaGate _gate = Substitute.For<ITramiteFirmaGate>();
    private readonly IFirmaPendienteNotifier _notifier = Substitute.For<IFirmaPendienteNotifier>();
    private readonly UltimoBloqueoFirma _bloqueo = new();

    public EnviarAlOtFirmaPrimeroTests()
    {
        _catalog.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", Arg.Any<CancellationToken>())
            .Returns(new ConsultationTemplate { Code = "RUNT_VEHICLE", ExternalRefs = $"{{\"provider\":\"{ProviderKey}\"}}" });
        _registry.Resolve(ProviderKey).Returns(_provider);
        _provider.Key.Returns(ProviderKey);
    }

    private EnviarAlOtHandler Sut() =>
        new(_repo, _lifecycle, new ValidateSoatViaRuntHandler(_repo, _catalog, _registry), _policy,
            firmaGate: _gate, firmaNotifier: _notifier, ultimoBloqueo: _bloqueo);

    private ProcedureInstance Asignado()
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013194",
            Status = TramiteEstado.Asignado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    [Fact]
    public async Task SinFirma_Responde409_SinConsultarElRunt_NiPersistirChecks_NiTransicionar()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Asignado();
        _gate.PartesSinFirmaAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(["comprador"]);
        _notifier.NotificarAsync(i.Id, i.TenantId, "comprador", Arg.Any<CancellationToken>())
            .Returns(FirmaNotificacionEstados.Enviada);

        var (resultado, error, warning) = await Sut().HandleAsync(
            i.Id, i.TenantId, null, new EnviarAlOtRequest(SoatPagado: true, ImpuestoDepartamentalPagado: true), ct);

        resultado.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.FirmaPendiente);
        warning.Should().BeNull();
        _bloqueo.PartesSinFirma.Should().Equal(new ParteSinFirma("comprador", FirmaNotificacionEstados.Enviada));

        // Ningún efecto de SOAT ni de envío.
        await _policy.DidNotReceive().IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _provider.DidNotReceive().ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>());
        i.FieldValues.Should().BeEmpty("los checks SOAT/impuesto no se escriben sin firma");
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
        await _lifecycle.DidNotReceive().TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinFirma_NotificadorQueFalla_SigueSiendo409()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Asignado();
        _gate.PartesSinFirmaAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(["vendedor"]);
        _notifier.NotificarAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("proveedor caído"));

        var (_, error, _) = await Sut().HandleAsync(i.Id, i.TenantId, null, new EnviarAlOtRequest(), ct);

        error.Should().Be(TramiteEstadoErrores.FirmaPendiente);
        _bloqueo.PartesSinFirma.Should().Equal(new ParteSinFirma("vendedor", FirmaNotificacionEstados.Fallida));
    }

    [Fact]
    public async Task Firmado_SigueConElRuntYLaTransicion()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Asignado();
        _gate.PartesSinFirmaAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns([]);
        _policy.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultationResult(
                ProviderKey, "ok", [new ConsultationCheck("soat", "SOAT", "pass", "runt", null)], []));
        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(TramiteTransitionOutcome.Fail(TramiteEstadoErrores.ConflictoConcurrencia));

        var (_, error, _) = await Sut().HandleAsync(i.Id, i.TenantId, null, new EnviarAlOtRequest(), ct);

        // Pasó el gate de firma: llegó a consultar la política del SOAT y a pedir la transición.
        error.Should().Be(TramiteEstadoErrores.ConflictoConcurrencia);
        await _policy.Received(1).IsEnabledAsync(i.TenantId, Arg.Any<CancellationToken>());
        await _lifecycle.Received(1).TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>());
        _notifier.ReceivedCalls().Should().BeEmpty();
    }
}
