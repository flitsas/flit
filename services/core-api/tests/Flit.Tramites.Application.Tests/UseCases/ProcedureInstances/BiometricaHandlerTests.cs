using System.Text;
using Flit.Tramites.Application.Biometrics;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

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
        _list = new ListBiometriaHandler(_repo);
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

        public void Delete(string storagePath) { }
    }

    private static ProcedureInstance Instance(
        Guid id, Guid tenantId, string status = ProcedureInstanceStatus.Draft) =>
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
        result.Validation.Estado.Should().Be(BiometricEstados.Enviado);
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
            Id = Guid.NewGuid(), Parte = null, Estado = BiometricEstados.Enviado,
            TokenHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow,
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
            Parte = null,
            Nombre = "Juan",
            TipoDoc = "CC",
            Documento = "123",
            Email = "j@x.com",
            Estado = estado,
            TokenHash = BiometricToken.Hash(token),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            Intentos = intentos,
            MaxIntentos = maxIntentos,
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
        v.Estado.Should().Be(BiometricEstados.Aprobado);
        v.ValidadoAt.Should().NotBeNull();
        v.Intentos.Should().Be(1);
        v.FotoRostroPath.Should().NotBeNull();
        v.FotoCedulaFrontalPath.Should().NotBeNull();
        v.FotoCedulaReversoPath.Should().NotBeNull();
        v.Detalle.Should().Contain("mock");
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
        v.Estado.Should().Be(BiometricEstados.Rechazado);
        v.ValidadoAt.Should().BeNull();
        v.Intentos.Should().Be(1);
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
        v.Intentos.Should().Be(2);
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
        v.Estado.Should().Be(BiometricEstados.Expirado);
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
        v.Estado.Should().Be(BiometricEstados.Expirado);
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
            Id = Guid.NewGuid(), Parte = "comprador", Nombre = "A", TipoDoc = "CC", Documento = "1", Email = "a@x.com",
            Estado = BiometricEstados.Aprobado, TokenHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _list.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle().Which.Estado.Should().Be(BiometricEstados.Aprobado);
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
        result!.Estado.Should().Be(BiometricEstados.Aprobado);
        result.Score.Should().Be(95);
        result.Parte.Should().Be("comprador");
        result.Nombre.Should().Be("Maria Compradora");
        result.Documento.Should().Be("999");
        instance.BiometricValidations.Should().ContainSingle()
            .Which.Detalle.Should().Contain("mock");
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
            Id = Guid.NewGuid(), Parte = "comprador", Estado = BiometricEstados.Aprobado, Score = 95,
            Nombre = "Maria Compradora", TipoDoc = "CC", Documento = "999", Email = "maria@x.com",
            TokenHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _simular.HandleAsync(id, tenant, parte: "comprador", ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(BiometricEstados.Aprobado);
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
            Id = Guid.NewGuid(), Parte = "comprador", Estado = BiometricEstados.Rechazado,
            Nombre = "Old", TipoDoc = "CC", Documento = "1", Email = "old@x.com",
            TokenHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.BiometricValidations.Add(rejected);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _simular.HandleAsync(id, tenant, parte: "comprador", ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(BiometricEstados.Aprobado);
        result.Score.Should().Be(95);
        instance.BiometricValidations.Should().ContainSingle(); // reusa, no crea
        rejected.Estado.Should().Be(BiometricEstados.Aprobado);
        rejected.Nombre.Should().Be("Maria Compradora"); // datos refrescados del actor
        await _repo.Received(1).SaveChangesAsync(ct);
    }
}
