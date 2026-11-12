using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Caracterización: identidad APROBADA cuya vigencia de 30 días ya pasó (<c>valid_until</c> en el pasado), dentro del
/// MISMO trámite. Fija el comportamiento ACTUAL de las cuatro vías por las que se podría volver a validar a la persona:
/// <list type="number">
///   <item>Asistente, paso de firmas (<see cref="IniciarKyverumVerifyHandler.HandleAsync"/>).</item>
///   <item>Renovación automática (<see cref="EnsureIdentityAndNotifyHandler"/>).</item>
///   <item>Módulo de trámites, reenvío administrativo (<see cref="AdminReenviarValidacionIdentidadHandler"/>).</item>
/// </list>
/// Ninguna deja revalidar: la fila <c>aprobado</c> vencida no cuenta como vigente, pero sigue bloqueando el inicio de una
/// nueva (<c>biometria_activa</c>) y el reenvío la excluye por estar aprobada.
/// </summary>
public sealed class AprobadaVencidaRevalidacionTests
{
    private const string Documento = "1020304050";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();

    public AprobadaVencidaRevalidacionTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        _vault.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult("kyv_new", "https://capture/kyv_new", "whsec", "pending", "{}", null));
    }

    /// <summary>Matrícula en borrador con el comprador y su validación aprobada hace 40 días (vencida).</summary>
    private (ProcedureInstance Instance, ProcedureInstanceBiometricValidation Vencida) TramiteConAprobadaVencida(
        string status = TramiteEstado.Borrador)
    {
        var now = DateTimeOffset.UtcNow;
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000040",
            Status = status,
            CreatedAt = now.AddDays(-45),
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instance.Id,
            ActorType = BiometricRules.ParteComprador,
            FullName = "Ana Ríos",
            DocumentType = "CC",
            DocumentNumber = Documento,
            PersonType = "natural",
            Email = "ana@prueba.test",
            Ordinal = 1,
        });

        var validadaEl = now.AddDays(-40);
        var vencida = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = BiometricRules.ParteComprador,
            Name = "Ana Ríos",
            DocumentType = "CC",
            DocumentNumber = Documento,
            Email = "ana@prueba.test",
            RegisteredEmail = "ana@prueba.test",
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = "kyv_old",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = validadaEl.AddHours(24),
            CreatedAt = validadaEl.AddMinutes(-10),
            ValidatedAt = validadaEl,
            ValidUntil = BiometricRules.FechaFinVigencia(validadaEl),
            ApprovalOrigin = BiometricApprovalOrigins.Automatica,
        };
        instance.BiometricValidations.Add(vencida);

        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdAsync(instance.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetBiometricByIdWithPersonAsync(vencida.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(vencida);
        return (instance, vencida);
    }

    private IniciarKyverumVerifyHandler IniciarHandler() =>
        new(_repo, _kyverum, new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(), Substitute.For<IIdentityValidationAuditLog>(), _vault);

    [Fact]
    public void Precondicion_LaFilaEstaAprobadaPeroYaNoEsVigente()
    {
        var (_, vencida) = TramiteConAprobadaVencida();

        vencida.Status.Should().Be(BiometricEstados.Aprobado);
        BiometricRules.EsAprobadaVigente(vencida, DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public async Task Asistente_IniciarNuevaValidacion_SeBloqueaConBiometriaActiva()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, vencida) = TramiteConAprobadaVencida();

        // El paso de firmas envía solo la parte; los datos salen del actor.
        var (result, error, _) = await IniciarHandler().HandleAsync(
            instance.Id, _tenantId, new IniciarBiometriaInput(BiometricRules.ParteComprador, "", "", "", ""), ct);

        error.Should().Be("biometria_activa", "el guardia por parte cuenta cualquier fila 'aprobado', vigente o no");
        result.Should().BeNull();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().ContainSingle().Which.Should().BeSameAs(vencida);
        vencida.Status.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task RenovacionAutomatica_RequiereValidacion_PeroReportaYaEnCursoSinEnviarCorreo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, _) = TramiteConAprobadaVencida();
        var notifier = new EnsureIdentityAndNotifyHandler(
            new EnsureIdentityHandler(_repo, _vault),
            IniciarHandler(),
            new SimularBiometriaHandler(_repo),
            new BiometricsProviderOptions { Provider = BiometricProviders.Kyverum });

        var (result, error) = await notifier.HandleAsync(instance.Id, _tenantId, BiometricRules.ParteComprador, ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion, "la identidad vencida ya no cubre la parte");
        result.Notificacion.Should().Be(IdentityNotificationOutcomes.YaEnCurso,
            "biometria_activa se traduce como sesión en curso aunque no haya ninguna");
        result.ValidationId.Should().BeNull();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModuloTramites_ReenvioAdministrativo_RechazaPorIdentidadAprobada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, vencida) = TramiteConAprobadaVencida(TramiteEstado.Entregado);
        var handler = new AdminReenviarValidacionIdentidadHandler(
            _repo, _kyverum, new BiometricsProviderOptions { Provider = BiometricProviders.Kyverum },
            new FakeWebhookSecretProtector(), Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>());

        var (result, error, _, _) = await handler.HandleAsync(
            new AdminReenviarValidacionIdentidadCommand(instance.Id, vencida.Id, _tenantId, null, Guid.NewGuid()), ct);

        error.Should().Be(AdminReenviarValidacionIdentidadHandler.IdentidadAprobada);
        result.Should().BeNull();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        vencida.Status.Should().Be(BiometricEstados.Aprobado);
    }
}
