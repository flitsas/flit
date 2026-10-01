using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13194 P3 — interruptor «Validar SOAT ante el RUNT» por compañía, con el validador REAL
/// (<see cref="ValidateSoatViaRuntHandler"/>) y el proveedor RUNT simulado.
///
/// <para>Semántica vigente (no cambia): opción ACTIVA ⇒ se consulta el RUNT y, sin SOAT vigente, se
/// continúa con advertencia; APAGADA ⇒ un RUNT que responde sin SOAT vigente bloquea.</para>
///
/// <para>Desvíos corregidos: (a) un RUNT que no reporta el SOAT (<c>unknown</c>) ya no degrada el
/// <c>soat_estado=vigente</c> de un PDF cargado a mano; un vencido explícito del RUNT sí manda.
/// (b) una excepción del proveedor es «consulta sin respuesta»: el envío continúa.</para>
///
/// Uso de ejemplo:
/// <code>
/// var sut = new EnviarAlOtHandler(repo, lifecycle, new ValidateSoatViaRuntHandler(repo, catalog, registry), policy);
/// var (result, error, warning) = await sut.HandleAsync(id, tenantId, userId, new EnviarAlOtRequest(), ct);
/// </code>
/// </summary>
public sealed class EnviarAlOtSoatSoporteManualTests
{
    private const string ProviderKey = "runt-test";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITramiteLifecycleService _lifecycle = Substitute.For<ITramiteLifecycleService>();
    private readonly ICatalogRepository _catalog = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly IConsultationProvider _provider = Substitute.For<IConsultationProvider>();
    private readonly ISoatRuntValidationPolicy _policy = Substitute.For<ISoatRuntValidationPolicy>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Futuro => DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
    private static string Pasado => DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd");

    public EnviarAlOtSoatSoporteManualTests()
    {
        _catalog.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", Arg.Any<CancellationToken>())
            .Returns(new ConsultationTemplate { Code = "RUNT_VEHICLE", ExternalRefs = $"{{\"provider\":\"{ProviderKey}\"}}" });
        _registry.Resolve(ProviderKey).Returns(_provider);
        _provider.Key.Returns(ProviderKey);
    }

    private EnviarAlOtHandler Sut() =>
        new(_repo, _lifecycle, new ValidateSoatViaRuntHandler(_repo, _catalog, _registry), _policy);

    private void Opcion(bool activa) =>
        _policy.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(activa);

    private void RuntResponde(string soatStatus) =>
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultationResult(
                ProviderKey, "ok", [new ConsultationCheck("soat", "SOAT", soatStatus, "runt", null)], []));

    private ProcedureInstance Asignado(params (string Key, string Value, string Source)[] campos)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013194",
            Status = TramiteEstado.Asignado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (key, value, source) in campos)
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                FieldKey = key,
                ValueText = value,
                Source = source,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                instance.Status = call.Arg<TramiteTransitionCommand>().ToStatus;
                return TramiteTransitionOutcome.Ok(instance);
            });
        return instance;
    }

    private static ProcedureInstanceFieldValue Soat(ProcedureInstance i) =>
        i.FieldValues.Single(f => f.FieldKey == SoatGate.FieldKey);

    /// <summary>(1) Opción APAGADA + PDF manual vigente + RUNT sin SOAT ⇒ no bloquea y el estado sigue vigente.</summary>
    [Theory]
    [InlineData("ocr")]
    [InlineData("user")]
    public async Task Apagada_conPdfManualVigente_yRuntSinSoat_noBloqueaYConservaVigente(string origen)
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado(
            (SoatGate.FieldKey, SoatGate.Vigente, origen),
            ("soat_vencimiento", Futuro, origen));

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        Soat(instance).ValueText.Should().Be(SoatGate.Vigente);
        Soat(instance).Source.Should().Be(origen);
    }

    /// <summary>(2) Regresión de la regla: APAGADA + RUNT sin SOAT y sin PDF ⇒ bloquea.</summary>
    [Fact]
    public async Task Apagada_sinPdf_yRuntSinSoat_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado();

        var (result, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
        Soat(instance).ValueText.Should().Be(SoatGate.Unknown);
        await _lifecycle.DidNotReceiveWithAnyArgs().TransitionAsync(default!, Ct);
    }

    /// <summary>Edge: el soporte manual con vencimiento pasado no cuenta como vigente ⇒ con APAGADA bloquea.</summary>
    [Fact]
    public async Task Apagada_conPdfManualVencidoPorFecha_yRuntSinSoat_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado(
            (SoatGate.FieldKey, SoatGate.Vigente, "ocr"),
            ("soat_vencimiento", Pasado, "ocr"));

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
    }

    /// <summary>Edge: un vigente que vino de una consulta anterior no es soporte manual: el RUNT actual manda.</summary>
    [Fact]
    public async Task Apagada_conVigenteDeConsultaAnterior_yRuntSinSoat_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado((SoatGate.FieldKey, SoatGate.Vigente, "consultation"));

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
    }

    /// <summary>Contrato: un vencido EXPLÍCITO del RUNT (fail) manda sobre el PDF manual ⇒ con APAGADA bloquea.</summary>
    [Fact]
    public async Task Apagada_conPdfManualVigente_yRuntVencido_bloqueaYRegistraVencido()
    {
        Opcion(activa: false);
        RuntResponde("fail");
        var instance = Asignado(
            (SoatGate.FieldKey, SoatGate.Vigente, "ocr"),
            ("soat_vencimiento", Futuro, "ocr"));

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
        Soat(instance).ValueText.Should().Be(SoatGate.Vencido);
    }

    /// <summary>(3) Opción ACTIVA + RUNT sin SOAT ⇒ continúa con advertencia.</summary>
    [Fact]
    public async Task Activa_runtSinSoat_continuaConAdvertencia()
    {
        Opcion(activa: true);
        RuntResponde("unknown");
        var instance = Asignado();

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().Be(EnviarAlOtHandler.SoatNoVigenteAdvertencia);
        result!.Status.Should().Be(TramiteEstado.Entregado);
    }

    /// <summary>(4) El proveedor RUNT lanza ⇒ consulta sin respuesta: el envío continúa (aun con la opción APAGADA).</summary>
    [Fact]
    public async Task ProveedorLanza_elEnvioContinuaSinBloquearNiAdvertir()
    {
        Opcion(activa: false);
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("RUNT caído"));
        var instance = Asignado();

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        instance.FieldValues.Should().NotContain(f => f.FieldKey == SoatGate.FieldKey);
    }

    /// <summary>Contrato del validador: la excepción del proveedor se traduce a <c>provider_error</c>.</summary>
    [Fact]
    public async Task Validador_proveedorLanza_devuelveProviderError()
    {
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("payload inesperado"));
        var instance = Asignado();

        var (result, error) = await new ValidateSoatViaRuntHandler(_repo, _catalog, _registry)
            .HandleAsync(instance.Id, instance.TenantId, Ct);

        result.Should().BeNull();
        error.Should().Be("provider_error");
    }

    /// <summary>Edge: la cancelación NO se traga — se propaga.</summary>
    [Fact]
    public async Task Validador_cancelacion_sePropaga()
    {
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var instance = Asignado();

        await new ValidateSoatViaRuntHandler(_repo, _catalog, _registry)
            .Invoking(h => h.HandleAsync(instance.Id, instance.TenantId, Ct))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
