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

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class IniciarKyverumVerifyHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IniciarKyverumVerifyHandler _handler;

    public IniciarKyverumVerifyHandlerTests() =>
        _handler = new IniciarKyverumVerifyHandler(_repo, _kyverum, _protector, _events);

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status = ProcedureInstanceStatus.Draft) =>
        new()
        {
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

    private void StubProviderOk(string verificationId = "kyv_123", string captureUrl = "https://capture/kyv_123", string secret = "whsec_abc") =>
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(verificationId, captureUrl, secret, "pending", "{\"verification_id\":\"" + verificationId + "\"}"));

    [Fact]
    public async Task Iniciar_HappyPath_PersistsKyverumFieldsAndEmitsRequested()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        StubProviderOk();

        var (result, error) = await _handler.HandleAsync(id, tenant, Input(), ct);

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

        var (result, error) = await _handler.HandleAsync(id, tenant, Input(), ct);

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

        var (_, error) = await _handler.HandleAsync(id, tenant, Input(), ct);

        error.Should().Be("proveedor_error");
    }

    [Fact]
    public async Task Iniciar_NotDraft_Returns409_AndDoesNotCallProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant, status: "submitted"));

        var (_, error) = await _handler.HandleAsync(id, tenant, Input(), ct);

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

        var (_, error) = await _handler.HandleAsync(id, tenant, Input(parte: "comprador"), ct);

        error.Should().Be("biometria_activa");
    }

    [Fact]
    public async Task Iniciar_InvalidParte_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Input(parte: "tercero"), ct);
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
        var (result, error) = await _handler.HandleAsync(id, tenant, ParteOnlyInput(), ct);

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

        var (_, error) = await _handler.HandleAsync(id, tenant, ParteOnlyInput(), ct);

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

        var (_, error) = await _handler.HandleAsync(id, tenant, ParteOnlyInput(), ct);

        error.Should().Be("datos_incompletos");
    }
}
