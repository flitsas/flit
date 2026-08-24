using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class IniciarKyverumVerifyHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IniciarKyverumVerifyHandler _handler;

    public IniciarKyverumVerifyHandlerTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        _handler = new IniciarKyverumVerifyHandler(
            _repo, _kyverum, _protector, _events, Substitute.For<IIdentityValidationAuditLog>());
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status = TramiteEstado.Borrador) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static IniciarBiometriaInput Input(string? parte = "comprador") =>
        new(parte, "Juan Perez", "CC", "123456", "juan@x.com");

    /// <summary>Body sin datos del sujeto: el wizard envía SOLO la parte (los datos salen del actor).</summary>
    private static IniciarBiometriaInput ParteOnlyInput(string? parte = "comprador") =>
        new(parte, "", "", "", "");

    private static void AddActor(ProcedureInstance instance, string actorType, string? email = "actor@x.com") =>
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = actorType,
            FullName = "Pedro Actor",
            DocumentType = "CC",
            DocumentNumber = "99887766",
            Email = email,
        });

    private void StubProviderOk(string verificationId = "kyv_123", string captureUrl = "https://capture/kyv_123", string secret = "whsec_abc", DateTimeOffset? expiresAt = null) =>
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(verificationId, captureUrl, secret, "pending", "{\"verification_id\":\"" + verificationId + "\"}", expiresAt));

    [Fact]
    public async Task Iniciar_HappyPath_PersistsKyverumFieldsAndEmitsRequested()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        StubProviderOk();

        var (result, error, _) = await _handler.HandleAsync(id, tenant, Input(), ct);

        error.Should().BeNull();
        result!.CaptureUrl.Should().Be("https://capture/kyv_123");
        result.Validation.Provider.Should().Be(BiometricProviders.Kyverum);
        result.Validation.Status.Should().Be(BiometricEstados.EnProceso);
        // AC9: captureUrl expuesta en kyverum + en_proceso.
        result.Validation.CaptureUrl.Should().Be("https://capture/kyv_123");

        var v = instance.BiometricValidations.Should().ContainSingle().Subject;
        v.KyverumVerificationId.Should().Be("kyv_123");
        // El secreto se persiste "cifrado" (prefijo del protector), nunca en claro.
        v.WebhookSecretEncrypted.Should().Be("prot::whsec_abc").And.StartWith("prot::");

        _repo.Received(1).Add(Arg.Any<ProcedureInstanceBiometricValidation>());
        await _repo.Received(1).SaveChangesAsync(ct);
        await _events.Received(1).PublishAsync(Arg.Is<IdentityValidationRequested>(e =>
            e.ValidationId == v.Id && e.Provider == BiometricProviders.Kyverum && e.ProviderVerificationId == "kyv_123"), ct);
    }

    [Fact]
    public async Task Iniciar_PersistsProviderExpiresAt_WhenKyverumReportsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        // Kyverum informa el vencimiento REAL del enlace: debe persistirse tal cual (no el TTL local +24h).
        var kyvExpiry = DateTimeOffset.UtcNow.AddMinutes(20);
        StubProviderOk(expiresAt: kyvExpiry);

        var (_, error, _) = await _handler.HandleAsync(id, tenant, Input(), ct);

        error.Should().BeNull();
        var v = instance.BiometricValidations.Should().ContainSingle().Subject;
        v.ExpiresAt.Should().Be(kyvExpiry);
    }

    [Fact]
    public async Task Iniciar_FallsBackToLocalTtl_WhenKyverumOmitsExpiresAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        StubProviderOk(); // sin expiresAt

        var (_, error, _) = await _handler.HandleAsync(id, tenant, Input(), ct);

        error.Should().BeNull();
        var v = instance.BiometricValidations.Should().ContainSingle().Subject;
        // Fallback al TTL local (24h) cuando el proveedor no reporta expiresAt.
        v.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(BiometricRules.TokenTtlHoras), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Iniciar_ExpiredEnProceso_DoesNotBlock_ExpiresOldAndCreatesNew()
    {
        // El enlace de la validación previa venció (en_proceso + expires_at en el pasado): NO debe bloquear;
        // se terminaliza la vieja como `expirado` y se crea una nueva (nuevo enlace de captura).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        var vencida = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.EnProceso,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5), // vencida
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Provider = BiometricProviders.Kyverum,
        };
        instance.BiometricValidations.Add(vencida);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        StubProviderOk(verificationId: "kyv_new", captureUrl: "https://capture/kyv_new");

        var (result, error, _) = await _handler.HandleAsync(id, tenant, Input(parte: "comprador"), ct);

        error.Should().BeNull();
        result!.CaptureUrl.Should().Be("https://capture/kyv_new");
        // La vieja quedó expirada; existe la nueva en_proceso.
        vencida.Status.Should().Be(BiometricEstados.Expirado);
        instance.BiometricValidations.Should().HaveCount(2);
        instance.BiometricValidations.Should().ContainSingle(v =>
            v.Status == BiometricEstados.EnProceso && v.KyverumVerificationId == "kyv_new");
    }

    [Fact]
    public async Task Iniciar_ActiveEnProcesoNotExpired_ReturnsConflict()
    {
        // Una validación en_proceso VIGENTE (enlace no vencido) sigue bloqueando el reenvío.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.EnProceso,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), // vigente
            CreatedAt = DateTimeOffset.UtcNow,
            Provider = BiometricProviders.Kyverum,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (_, error, _) = await _handler.HandleAsync(id, tenant, Input(parte: "comprador"), ct);

        error.Should().Be("biometria_activa");
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Iniciar_IdentidadVigenteDeOtroTramite_Returns409ConflictSinLlamarProveedor()
    {
        // HU #11265 AC1
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        var vigenteId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _repo.FindVigenteApprovedByDocumentAsync(tenant, "CC", "123456", Arg.Any<DateTimeOffset>(), ct)
            .Returns(new ProcedureInstanceBiometricValidation
            {
                Id = vigenteId,
                TenantId = tenant,
                ProcedureInstanceId = Guid.NewGuid(),
                DocumentType = "CC",
                DocumentNumber = "123456",
                Status = BiometricEstados.Aprobado,
                ValidatedAt = now.AddDays(-3),
                ValidUntil = now.AddDays(27),
                TokenHash = "h",
                ExpiresAt = now.AddHours(1),
                CreatedAt = now.AddDays(-3),
            });

        var (_, error, conflict) = await _handler.HandleAsync(id, tenant, Input(parte: "comprador"), ct);

        error.Should().Be("biometria_activa");
        conflict.Should().NotBeNull();
        conflict!.Motivo.Should().Be(IdentitySendMotivo.IdentidadVigente);
        conflict.ValidationId.Should().Be(vigenteId);
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().BeEmpty();
    }

    [Fact]
    public async Task Iniciar_ProviderTransientError_EncolaParaReintento()
    {
        // Fallo TRANSITORIO del proveedor → NO devuelve error: encola la validación en pendiente_envio
        // para que el worker reintente el envío (cola de envío provider-agnostic).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("no disponible", transient: true));

        var (result, error, _) = await _handler.HandleAsync(id, tenant, Input(), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Queued.Should().BeTrue();
        result.CaptureUrl.Should().BeEmpty();
        result.Validation.Status.Should().Be(BiometricEstados.PendienteEnvio);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Iniciar_ProviderDefinitiveError_Returns502Code()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("rechazado", transient: false));

        var (_, error, _) = await _handler.HandleAsync(id, tenant, Input(), ct);

        error.Should().Be("proveedor_error");
    }

    [Fact]
    public async Task Iniciar_NotDraft_Returns409_AndDoesNotCallProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, status: "submitted"));

        var (_, error, _) = await _handler.HandleAsync(id, tenant, Input(), ct);

        error.Should().Be("not_draft");
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Iniciar_ActiveExists_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.Aprobado,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (_, error, _) = await _handler.HandleAsync(id, tenant, Input(parte: "comprador"), ct);

        error.Should().Be("biometria_activa");
    }

    [Fact]
    public async Task Iniciar_InvalidParte_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Input(parte: "tercero"), ct);
        error.Should().Be("parte_invalida");
    }

    [Fact]
    public async Task Iniciar_ParteOnly_ResolvesActorDataAndSendsToKyverum()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        AddActor(instance, "comprador", email: "actor@x.com");
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        StubProviderOk();

        // El wizard envía solo la parte: los datos del sujeto deben salir del actor del trámite.
        var (result, error, _) = await _handler.HandleAsync(id, tenant, ParteOnlyInput(), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        await _kyverum.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r =>
                r.Documento == "99887766" && r.Email == "actor@x.com" && r.Nombre == "Pedro Actor" && r.Parte == "comprador"),
            ct);
        var v = instance.BiometricValidations.Should().ContainSingle().Subject;
        v.Email.Should().Be("actor@x.com");
        v.DocumentNumber.Should().Be("99887766");
    }

    [Fact]
    public async Task Iniciar_ParteOnly_NoActor_ReturnsActorRequerido()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var (_, error, _) = await _handler.HandleAsync(id, tenant, ParteOnlyInput(), ct);

        error.Should().Be("actor_requerido");
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Iniciar_ActorWithoutEmail_ReturnsIncompleteData()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        AddActor(instance, "comprador", email: null);   // Kyverum necesita el correo para notificar.
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (_, error, _) = await _handler.HandleAsync(id, tenant, ParteOnlyInput(), ct);

        error.Should().Be("datos_incompletos");
    }
}
