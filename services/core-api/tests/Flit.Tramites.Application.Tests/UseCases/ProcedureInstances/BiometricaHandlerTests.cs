using System.Text;
using Flit.Tramites.Application.Biometrics;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class BiometricaHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly IBiometricScorer _scorer = new MockBiometricScorer();
    private readonly IniciarBiometriaHandler _iniciar;
    private readonly GetBiometriaByTokenHandler _getByToken;
    private readonly CompletarBiometriaHandler _completar;
    private readonly ListBiometriaHandler _list;
    private readonly SimularBiometriaHandler _simular;

    public BiometricaHandlerTests()
    {
        _iniciar = new IniciarBiometriaHandler(_repo);
        _getByToken = new GetBiometriaByTokenHandler(_repo);
        _completar = new CompletarBiometriaHandler(_repo, _storage, _scorer);
        _list = new ListBiometriaHandler(_repo, new BiometricsProviderOptions());
        _simular = new SimularBiometriaHandler(_repo);
    }

    private static ProcedureInstanceActor Actor(Guid tenant, string actorType) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = actorType,
            DocumentType = "CC",
            DocumentNumber = "999",
            FullName = "Maria Compradora",
            Email = "maria@x.com",
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}";
            Saved.Add(path);
            return new StoredFile(path, "deadbeef", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);
    }

    private static ProcedureInstance Instance(
        Guid id, Guid tenantId, string status = TramiteEstado.Borrador) =>
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

    private static IniciarBiometriaInput Input(string? parte = null) =>
        new(parte, "Juan Perez", "CC", "123456", "juan@x.com");

    private static MemoryStream Photo(string content = "img") =>
        new(Encoding.UTF8.GetBytes(content));

    // ── Iniciar ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Iniciar_HappyPath_ReturnsRawTokenAndPersists()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithBiometricsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _iniciar.HandleAsync(id, tenant, Input(), ct);

        error.Should().BeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.MagicLinkPath.Should().Be($"/biometric/{result.Token}");
        result.Validation.Status.Should().Be(BiometricEstados.Enviado);
        instance.BiometricValidations.Should().ContainSingle();
        // El hash persistido NO es el token crudo.
        instance.BiometricValidations.Single().TokenHash.Should().NotBe(result.Token);
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceBiometricValidation>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Iniciar_NotDraft_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAsync(id, tenant, ct).Returns(Instance(id, tenant, status: "submitted"));

        var (_, error) = await _iniciar.HandleAsync(id, tenant, Input(), ct);

        error.Should().Be("not_draft");
    }

    [Fact]
    public async Task Iniciar_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithBiometricsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _iniciar.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Input(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Iniciar_IncompleteData_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _iniciar.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), new IniciarBiometriaInput(null, "", "CC", "1", "a@b.com"), ct);

        error.Should().Be("datos_incompletos");
    }

    [Fact]
    public async Task Iniciar_InvalidParte_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _iniciar.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Input(parte: "tercero"), ct);

        error.Should().Be("parte_invalida");
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
            PartyRole = null,
            Status = BiometricEstados.Enviado,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _iniciar.HandleAsync(id, tenant, Input(), ct);

        error.Should().Be("biometria_activa");
    }

    // ── Completar ──────────────────────────────────────────────────────────────

    private (ProcedureInstanceBiometricValidation v, string token) Seed(
        string estado = BiometricEstados.Enviado,
        int intentos = 0,
        int maxIntentos = 5,
        DateTimeOffset? expiresAt = null)
    {
        var token = "raw-token-abc";
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            PartyRole = null,
            Name = "Juan",
            DocumentType = "CC",
            DocumentNumber = "123",
            Email = "j@x.com",
            Status = estado,
            TokenHash = BiometricToken.Hash(token),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            Attempts = intentos,
            MaxAttempts = maxIntentos,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetBiometricByTokenHashAsync(BiometricToken.Hash(token), Arg.Any<CancellationToken>()).Returns(v);
        return (v, token);
    }

    [Fact]
    public async Task Completar_ThreePhotos_Approves()
    {
        var ct = TestContext.Current.CancellationToken;
        var (v, token) = Seed();

        var (result, error) = await _completar.HandleAsync(
            token, new CompletarBiometriaInput(Photo(), Photo(), Photo()), ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(BiometricEstados.Aprobado);
        result.Score.Should().BeGreaterThanOrEqualTo(BiometricRules.ThresholdAprobacion);
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().NotBeNull();
        v.Attempts.Should().Be(1);
        v.FacePhotoPath.Should().NotBeNull();
        v.IdFrontPhotoPath.Should().NotBeNull();
        v.IdBackPhotoPath.Should().NotBeNull();
        v.Detail.Should().Contain("mock");
        _storage.Saved.Should().HaveCount(3);
        await _repo.Received().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Completar_FewerThanThreePhotos_Rejects()
    {
        var ct = TestContext.Current.CancellationToken;
        var (v, token) = Seed();

        var (result, error) = await _completar.HandleAsync(
            token, new CompletarBiometriaInput(Photo(), null, null), ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(BiometricEstados.Rechazado);
        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.ValidatedAt.Should().BeNull();
        v.Attempts.Should().Be(1);
        result.Motivo.Should().Contain("cedula_frontal");
    }

    [Fact]
    public async Task Completar_RetryAfterReject_IncrementsIntentos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (v, token) = Seed(estado: BiometricEstados.Rechazado, intentos: 1);

        var (result, error) = await _completar.HandleAsync(
            token, new CompletarBiometriaInput(Photo(), Photo(), Photo()), ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(BiometricEstados.Aprobado);
        v.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Completar_IntentosAgotados_Returns429()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed(estado: BiometricEstados.Rechazado, intentos: 5, maxIntentos: 5);

        var (_, error) = await _completar.HandleAsync(
            token, new CompletarBiometriaInput(Photo(), Photo(), Photo()), ct);

        error.Should().Be("intentos_agotados");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Completar_Expired_MarksExpiredAndReturnsGone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (v, token) = Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        var (_, error) = await _completar.HandleAsync(
            token, new CompletarBiometriaInput(Photo(), Photo(), Photo()), ct);

        error.Should().Be("expirada");
        v.Status.Should().Be(BiometricEstados.Expirado);
    }

    [Fact]
    public async Task Completar_AlreadyApproved_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed(estado: BiometricEstados.Aprobado);

        var (_, error) = await _completar.HandleAsync(
            token, new CompletarBiometriaInput(Photo(), Photo(), Photo()), ct);

        error.Should().Be("estado_invalido");
    }

    [Fact]
    public async Task Completar_TokenNotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByTokenHashAsync(Arg.Any<string>(), ct).Returns((ProcedureInstanceBiometricValidation?)null);

        var (_, error) = await _completar.HandleAsync(
            "unknown", new CompletarBiometriaInput(Photo(), Photo(), Photo()), ct);

        error.Should().Be("not_found");
    }

    // ── GetByToken (público) ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByToken_NotFound_DoesNotLeak()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByTokenHashAsync(Arg.Any<string>(), ct).Returns((ProcedureInstanceBiometricValidation?)null);

        var (result, error) = await _getByToken.HandleAsync("whatever", ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByToken_PastExpiry_MarksExpired()
    {
        var ct = TestContext.Current.CancellationToken;
        var (v, token) = Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        var (result, error) = await _getByToken.HandleAsync(token, ct);

        error.Should().BeNull();
        result!.Expired.Should().BeTrue();
        result.Estado.Should().Be(BiometricEstados.Expirado);
        v.Status.Should().Be(BiometricEstados.Expirado);
    }

    [Fact]
    public async Task GetByToken_Active_ReturnsPublicViewWithoutPiiEnumeration()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = Seed();

        var (result, error) = await _getByToken.HandleAsync(token, ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(BiometricEstados.Enviado);
        result.Expired.Should().BeFalse();
        result.MaxIntentos.Should().Be(BiometricRules.MaxIntentos);
    }

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithBiometricsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _list.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task List_ReturnsValidations()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "A",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "a@x.com",
            Status = BiometricEstados.Aprobado,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _list.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle().Which.Status.Should().Be(BiometricEstados.Aprobado);
    }

    // ── AC4 (#10234): motivo de rechazo sanitizado en el DTO ─────────────────────

    [Fact]
    public async Task List_RejectedValidation_ExposesSanitizedMotivoFromDetalle()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "A",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "a@x.com",
            Status = BiometricEstados.Rechazado,
            // Detail del scorer mock (ya sanitizado, sin PII).
            Detail = """{"score":30,"aprobado":false,"motivo":"Faltan fotos: rostro","scorer":"mock"}""",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _list.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle()
            .Which.RejectionReason.Should().Be("Faltan fotos: rostro");
    }

    [Fact]
    public async Task List_KyverumRejected_DerivesMotivoFromCoincidencias()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "A",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "a@x.com",
            Status = BiometricEstados.Rechazado,
            Provider = BiometricProviders.Kyverum,
            // Payload sanitizado de Kyverum: el documento no coincide.
            ProviderPayload = """{"status":"rejected","score":20,"coincidencias":{"documento":false,"nombre":true}}""",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _list.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle()
            .Which.RejectionReason.Should().Be("La verificación del documento no fue exitosa.");
    }

    [Fact]
    public async Task List_NonRejectedValidation_HasNoMotivoRechazo()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "A",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "a@x.com",
            Status = BiometricEstados.Aprobado,
            Detail = """{"motivo":"no debe filtrarse"}""",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _list.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle().Which.RejectionReason.Should().BeNull();
    }

    // ── Simular (mock, sin fotos) ────────────────────────────────────────────────

    [Fact]
    public async Task Simular_DefaultParte_ApprovesWithScore95FromActor()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(Actor(tenant, "comprador"));
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        // parte vacío → comprador (matrícula, única parte).
        var (result, error) = await _simular.HandleAsync(id, tenant, parte: null, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(BiometricEstados.Aprobado);
        result.Score.Should().Be(95);
        result.PartyRole.Should().Be("comprador");
        result.Name.Should().Be("Maria Compradora");
        result.DocumentNumber.Should().Be("999");
        instance.BiometricValidations.Should().ContainSingle()
            .Which.Detail.Should().Contain("mock");
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceBiometricValidation>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Simular_AlreadyApproved_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(Actor(tenant, "comprador"));
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.Aprobado,
            Score = 95,
            Name = "Maria Compradora",
            DocumentType = "CC",
            DocumentNumber = "999",
            Email = "maria@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _simular.HandleAsync(id, tenant, parte: "comprador", ct);

        error.Should().BeNull();
        result!.Status.Should().Be(BiometricEstados.Aprobado);
        instance.BiometricValidations.Should().ContainSingle(); // no duplica
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
        await _repo.DidNotReceive().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Simular_NoActor_ReturnsActorRequerido()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant); // sin actores
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _simular.HandleAsync(id, tenant, parte: "comprador", ct);

        error.Should().Be("actor_requerido");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Simular_InvalidParte_ReturnsParteInvalida()
    {
        var ct = TestContext.Current.CancellationToken;
        var (result, error) = await _simular.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), parte: "tercero", ct);

        error.Should().Be("parte_invalida");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Simular_InstanceNotFound_ReturnsInstanceNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithBiometricsAndActorsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _simular.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), parte: "comprador", ct);

        error.Should().Be("instance_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Simular_ReusesExistingRejected_FlipsToApproved()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(Actor(tenant, "comprador"));
        var rejected = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.Rechazado,
            Name = "Old",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "old@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.BiometricValidations.Add(rejected);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _simular.HandleAsync(id, tenant, parte: "comprador", ct);

        error.Should().BeNull();
        result!.Status.Should().Be(BiometricEstados.Aprobado);
        result.Score.Should().Be(95);
        instance.BiometricValidations.Should().ContainSingle(); // reusa, no crea
        rejected.Status.Should().Be(BiometricEstados.Aprobado);
        rejected.Name.Should().Be("Maria Compradora"); // datos refrescados del actor
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    // ── Listado transversal del tenant (#10234, submódulo Validaciones) ───────────

    /// <summary>Construye una validación con su instancia padre (referencia/modalidad) para el listado transversal.</summary>
    private static ProcedureInstanceBiometricValidation TenantVal(
        Guid tenant,
        string estado,
        string provider = BiometricProviders.Mock,
        int? score = null,
        string? detalle = null,
        string? providerPayload = null,
        DateTimeOffset? expiresAt = null,
        string reference = "TRM-2026-000007",
        string modalidad = "matricula_inicial") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "Ana",
            DocumentType = "CC",
            DocumentNumber = "123456",
            Email = "ana@x.com",
            Status = estado,
            Provider = provider,
            Score = score,
            Detail = detalle,
            ProviderPayload = providerPayload,
            TokenHash = "h",
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureInstance = new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ReferenceNumber = reference,
                ModalidadEntrada = modalidad,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

    [Fact]
    public async Task ListTenant_MapsRowsAndComputesStatsFromCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);

        var rows = new List<ProcedureInstanceBiometricValidation>
        {
            TenantVal(tenant, BiometricEstados.Aprobado, score: 95, reference: "TRM-2026-000001", modalidad: "traspaso"),
            TenantVal(tenant, BiometricEstados.Rechazado, provider: BiometricProviders.Kyverum,
                providerPayload: """{"coincidencias":{"documento":false,"nombre":true}}"""),
        };
        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(rows);
        // KPIs exactos vienen del conteo agrupado, no de las filas (que están acotadas).
        _repo.CountBiometricValidationsByEstadoAsync(tenant, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int>
        {
            [BiometricEstados.Aprobado] = 3,
            [BiometricEstados.Enviado] = 1,
            [BiometricEstados.EnProceso] = 2,
            [BiometricEstados.Rechazado] = 1,
            [BiometricEstados.Expirado] = 1,
        });

        var (result, error) = await handler.HandleAsync(tenant, ct: ct);

        error.Should().BeNull();
        result!.Validations.Should().HaveCount(2);
        var aprobada = result.Validations[0];
        aprobada.ReferenceNumber.Should().Be("TRM-2026-000001");
        aprobada.Modalidad.Should().Be("traspaso");
        aprobada.Status.Should().Be(BiometricEstados.Aprobado);
        aprobada.RejectionReason.Should().BeNull();
        result.Validations[1].RejectionReason.Should().Be("La verificación del documento no fue exitosa.");

        result.Stats.Total.Should().Be(8);
        result.Stats.Aprobadas.Should().Be(3);
        result.Stats.EnProceso.Should().Be(3); // enviado(1) + en_proceso(2)
        result.Stats.Rechazadas.Should().Be(1);
        result.Stats.Expiradas.Should().Be(1);
    }

    [Fact]
    public async Task ListTenant_ComputesExpiredFlagLikeDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var past = DateTimeOffset.UtcNow.AddHours(-1);

        var rows = new List<ProcedureInstanceBiometricValidation>
        {
            TenantVal(tenant, BiometricEstados.Enviado, expiresAt: past),   // no aprobada + vencida → expired
            TenantVal(tenant, BiometricEstados.Aprobado, expiresAt: past),  // aprobada → nunca expired
        };
        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(rows);
        _repo.CountBiometricValidationsByEstadoAsync(tenant, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int>());

        var (result, error) = await handler.HandleAsync(tenant, ct: ct);

        error.Should().BeNull();
        result!.Validations[0].Expired.Should().BeTrue();
        result.Validations[1].Expired.Should().BeFalse();
    }

    [Fact]
    public async Task ListTenant_NoData_ReturnsEmptyWithZeroStats()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>());
        _repo.CountBiometricValidationsByEstadoAsync(tenant, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int>());

        var (result, error) = await handler.HandleAsync(tenant, ct: ct);

        error.Should().BeNull();
        result!.Validations.Should().BeEmpty();
        result.Stats.Total.Should().Be(0);
        result.Stats.Aprobadas.Should().Be(0);
    }

    [Fact]
    public async Task ListTenant_NoFilters_PassesNullFilterToRepository()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>());
        _repo.CountBiometricValidationsByEstadoAsync(tenant, null, Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int>());

        var (result, error) = await handler.HandleAsync(tenant, new TenantBiometricValidationListQuery(), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        // Página por defecto: skip 0, take 20 (DefaultPageSize), filtro null.
        await _repo.Received(1).ListBiometricValidationsByTenantAsync(
            tenant, 0, TenantBiometricValidationListQuery.DefaultPageSize, null, Arg.Any<DateTimeOffset>(), ct);
        await _repo.Received(1).CountBiometricValidationsByEstadoAsync(tenant, null, Arg.Any<DateTimeOffset>(), ct);
    }

    [Fact]
    public async Task ListTenant_FilterByEstado_PassesFilterToRepository()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(Status: BiometricEstados.Aprobado);
        var expectedFilter = query.ToFilter();

        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>());
        _repo.CountBiometricValidationsByEstadoAsync(tenant, Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int> { [BiometricEstados.Aprobado] = 2 });

        var (result, error) = await handler.HandleAsync(tenant, query, ct);

        error.Should().BeNull();
        result!.Stats.Aprobadas.Should().Be(2);
        await _repo.Received(1).ListBiometricValidationsByTenantAsync(
            tenant,
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Is<BiometricValidationListFilter>(f => f.Status == expectedFilter.Status),
            Arg.Any<DateTimeOffset>(),
            ct);
    }

    [Fact]
    public async Task ListTenant_CombinedReferenceAndNombre_PassesBothFilters()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(ReferenceNumber: "TRM-2026", Name: "Ana");

        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>
            {
                TenantVal(tenant, BiometricEstados.Aprobado, reference: "TRM-2026-000001"),
            });
        _repo.CountBiometricValidationsByEstadoAsync(tenant, Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int> { [BiometricEstados.Aprobado] = 1 });

        var (result, error) = await handler.HandleAsync(tenant, query, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle();
        await _repo.Received(1).ListBiometricValidationsByTenantAsync(
            tenant,
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Is<BiometricValidationListFilter>(f =>
                f.ReferenceNumber == "TRM-2026" && f.Name == "Ana"),
            Arg.Any<DateTimeOffset>(),
            ct);
    }

    [Fact]
    public async Task ListTenant_FilteredStats_ReflectFilteredCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(Modalidad: "traspaso");

        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>());
        _repo.CountBiometricValidationsByEstadoAsync(tenant, Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int>
            {
                [BiometricEstados.Aprobado] = 2,
                [BiometricEstados.Rechazado] = 1,
            });

        var (result, error) = await handler.HandleAsync(tenant, query, ct);

        error.Should().BeNull();
        result!.Stats.Total.Should().Be(3);
        result.Stats.Aprobadas.Should().Be(2);
        result.Stats.Rechazadas.Should().Be(1);
        await _repo.Received(1).CountBiometricValidationsByEstadoAsync(
            tenant,
            Arg.Is<BiometricValidationListFilter>(f => f.Modalidad == "traspaso"),
            Arg.Any<DateTimeOffset>(),
            ct);
    }

    [Fact]
    public async Task ListTenant_InvalidEstado_ReturnsValidationError()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(Status: "foo");

        var (result, error) = await handler.HandleAsync(Guid.NewGuid(), query, ct);

        result.Should().BeNull();
        error.Should().Contain("estado inválido");
        await _repo.DidNotReceive().ListBiometricValidationsByTenantAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListTenant_ScoreMinGreaterThanMax_ReturnsValidationError()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(ScoreMin: 90, ScoreMax: 10);

        var (result, error) = await handler.HandleAsync(Guid.NewGuid(), query, ct);

        result.Should().BeNull();
        error.Should().Contain("scoreMin");
        await _repo.DidNotReceive().ListBiometricValidationsByTenantAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListTenant_Pagination_ComputesSkipTakeAndReturnsMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(Page: 3, PageSize: 10);

        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>());
        _repo.CountBiometricValidationsByEstadoAsync(tenant, Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int> { [BiometricEstados.Aprobado] = 42 });

        var (result, error) = await handler.HandleAsync(tenant, query, ct);

        error.Should().BeNull();
        result!.Page.Should().Be(3);
        result.PageSize.Should().Be(10);
        result.Total.Should().Be(42); // total del conjunto completo (conteo agrupado), no solo la página
        // Página 3 con tamaño 10 → skip 20, take 10.
        await _repo.Received(1).ListBiometricValidationsByTenantAsync(
            tenant, 20, 10, Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct);
    }

    [Fact]
    public async Task ListTenant_PageSizeFueraDeRango_SeAcota()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        // 999 fuera de rango → se acota a 50; página 0 → se normaliza a 1 (skip 0).
        var query = new TenantBiometricValidationListQuery(Page: 0, PageSize: 999);
        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>());
        _repo.CountBiometricValidationsByEstadoAsync(tenant, Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new Dictionary<string, int>());

        var (result, _) = await handler.HandleAsync(tenant, query, ct);

        result!.PageSize.Should().Be(50);
        result.Page.Should().Be(1);
        await _repo.Received(1).ListBiometricValidationsByTenantAsync(
            tenant, 0, 50, Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct);
    }

    [Fact]
    public async Task ListTenant_FilterByMotivoRechazo_FiltersInMemoryAndDerivesStats()
    {
        // El filtro motivoRechazo se resuelve en memoria sobre el texto sanitizado (Detail/ProviderPayload
        // son jsonb y Postgres no permite ILIKE sobre jsonb). Solo deben quedar las rechazadas cuyo motivo
        // mostrado contiene el término; los KPIs reflejan ese subconjunto y NO se consulta el conteo en BD.
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(MotivoRechazo: "ilegible");

        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>
            {
                TenantVal(tenant, BiometricEstados.Rechazado, detalle: "{\"motivo\":\"Documento ilegible\"}"),
                TenantVal(tenant, BiometricEstados.Rechazado, detalle: "{\"motivo\":\"Rostro no coincide\"}"),
                TenantVal(tenant, BiometricEstados.Aprobado),
            });

        var (result, error) = await handler.HandleAsync(tenant, query, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle();
        result.Validations[0].RejectionReason.Should().Be("Documento ilegible");
        result.Stats.Total.Should().Be(1);
        result.Stats.Rechazadas.Should().Be(1);
        result.Stats.Aprobadas.Should().Be(0);
        await _repo.DidNotReceive().CountBiometricValidationsByEstadoAsync(
            Arg.Any<Guid>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListTenant_FilterByMotivoRechazo_MatchesKyverumDerivedText()
    {
        // Kyverum: el motivo mostrado es DERIVADO (no está literal en el payload). El filtro debe coincidir
        // sobre ese texto derivado (la versión anterior buscaba el JSON crudo y nunca coincidía).
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListTenantBiometricValidationsHandler(_repo);
        var query = new TenantBiometricValidationListQuery(MotivoRechazo: "documento no fue exitosa");

        _repo.ListBiometricValidationsByTenantAsync(tenant, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new List<ProcedureInstanceBiometricValidation>
            {
                TenantVal(tenant, BiometricEstados.Rechazado, provider: BiometricProviders.Kyverum,
                    providerPayload: "{\"coincidencias\":{\"documento\":false}}"),
            });

        var (result, error) = await handler.HandleAsync(tenant, query, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle();
        result.Validations[0].RejectionReason.Should().Be("La verificación del documento no fue exitosa.");
    }
}
