using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// Bug #13885 — una validación en vuelo de un trámite anulado, revocado o borrado quedaba congelada (Bug #13055) y el índice
/// único por documento bloqueaba toda validación nueva de la persona en el tenant (caso PDN FT2-0000181). Antes de llamar al
/// proveedor, el inicio cierra esas filas como <c>expirado</c>; si la que bloquea está viva, responde 409 con su estado y su
/// origen SIN crear nada en Kyverum (antes quedaba una verificación huérfana por intento).
/// </summary>
public sealed class Bug13885ValidacionEnVueloTramiteInactivoTests
{
    private const string Documento = "10741866";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();

    public Bug13885ValidacionEnVueloTramiteInactivoTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        _repo.ListInFlightBlockersByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _vault.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult("kyv_new", "https://capture/kyv_new", "whsec", "pending", "{}", null));
    }

    // ── Regla de dominio ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BiometricEstados.EnProceso, TramiteEstado.Anulado, false, true)]
    [InlineData(BiometricEstados.Enviado, TramiteEstado.Revocado, false, true)]
    [InlineData(BiometricEstados.PendienteEnvio, TramiteEstado.Anulado, false, true)]
    [InlineData(BiometricEstados.EnProceso, TramiteEstado.Borrador, true, true)]
    [InlineData(BiometricEstados.EnProceso, TramiteEstado.Borrador, false, false)]
    [InlineData(BiometricEstados.EnProceso, TramiteEstado.Entregado, false, false)]
    [InlineData(BiometricEstados.Aprobado, TramiteEstado.Anulado, false, false)]
    [InlineData(BiometricEstados.Rechazado, TramiteEstado.Revocado, false, false)]
    public void EsEnVueloDeTramiteInactivo_SoloEnVueloDeTramiteAnuladoRevocadoOBorrado(
        string estado, string estadoTramite, bool borrado, bool esperado)
    {
        var v = Fila(estado, Tramite(estadoTramite, borrado));

        BiometricRules.EsEnVueloDeTramiteInactivo(v).Should().Be(esperado);
    }

    [Fact]
    public void EsEnVueloDeTramiteInactivo_PrevalidacionSinTramite_NoAplica()
    {
        BiometricRules.EsEnVueloDeTramiteInactivo(Fila(BiometricEstados.EnProceso, tramite: null)).Should().BeFalse();
    }

    // ── Inicio desde el trámite (Kyverum) — caso FT2-0000181 ───────────────────────────────────────

    [Fact]
    public async Task Tramite_ZombieDeTramiteAnulado_SeCierraYSeCreaLaValidacionNueva()
    {
        var instance = TramiteBorradorConVendedor();
        var zombie = Fila(BiometricEstados.EnProceso, Tramite(TramiteEstado.Anulado));
        StubBloqueos(zombie);

        var (result, error, conflict) = await KyverumHandler().HandleAsync(
            instance.Id, Tenant, new IniciarBiometriaInput(BiometricRules.ParteVendedor, "", "", "", ""), Ct);

        error.Should().BeNull();
        conflict.Should().BeNull();
        result!.CaptureUrl.Should().Be("https://capture/kyv_new");
        zombie.Status.Should().Be(BiometricEstados.Expirado);
        instance.BiometricValidations.Should().ContainSingle(v =>
            v.Status == BiometricEstados.EnProceso && v.PartyRole == BiometricRules.ParteVendedor);
        await _kyverum.Received(1).StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e =>
                e.Stage == IdentityValidationAuditStages.Expired && e.ValidationId == zombie.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tramite_ZombieDeTramiteBorrado_SeCierraYSeCreaLaValidacionNueva()
    {
        var instance = TramiteBorradorConVendedor();
        var zombie = Fila(BiometricEstados.Enviado, Tramite(TramiteEstado.Borrador, borrado: true));
        StubBloqueos(zombie);

        var (_, error, _) = await KyverumHandler().HandleAsync(
            instance.Id, Tenant, new IniciarBiometriaInput(BiometricRules.ParteVendedor, "", "", "", ""), Ct);

        error.Should().BeNull();
        zombie.Status.Should().Be(BiometricEstados.Expirado);
    }

    [Fact]
    public async Task Tramite_BloqueoVivoEnOtroTramite_Responde409ConSuEstadoSinLlamarAKyverum()
    {
        // La precedencia solo mira la fila más reciente: una en vuelo más vieja de un trámite vivo no la ve, pero el índice
        // único sí. Antes: llamada a Kyverum, choque al guardar y 409 sin estado («Estado: disponible»).
        var instance = TramiteBorradorConVendedor();
        var viva = Fila(BiometricEstados.EnProceso, Tramite(TramiteEstado.Borrador));
        StubBloqueos(viva);

        var (result, error, conflict) = await KyverumHandler().HandleAsync(
            instance.Id, Tenant, new IniciarBiometriaInput(BiometricRules.ParteVendedor, "", "", "", ""), Ct);

        result.Should().BeNull();
        error.Should().Be("biometria_activa");
        conflict!.Motivo.Should().Be(IdentitySendMotivo.ValidacionEnVuelo);
        conflict.Status.Should().Be(BiometricEstados.EnProceso);
        conflict.ValidationId.Should().Be(viva.Id);
        conflict.Origen.Should().Be(IdentitySendOrigen.Tramite);
        viva.Status.Should().Be(BiometricEstados.EnProceso);
        instance.BiometricValidations.Should().BeEmpty();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenovacionAutomatica_ConZombieDeTramiteAnulado_EnviaElCorreo()
    {
        var instance = TramiteBorradorConVendedor();
        var zombie = Fila(BiometricEstados.EnProceso, Tramite(TramiteEstado.Anulado));
        StubBloqueos(zombie);
        var notifier = new EnsureIdentityAndNotifyHandler(
            new EnsureIdentityHandler(_repo, _vault),
            KyverumHandler(),
            new SimularBiometriaHandler(_repo),
            new BiometricsProviderOptions { Provider = BiometricProviders.Kyverum });

        var (result, error) = await notifier.HandleAsync(instance.Id, Tenant, BiometricRules.ParteVendedor, ct: Ct);

        error.Should().BeNull();
        result!.Notificacion.Should().Be(IdentityNotificationOutcomes.Enviada);
        zombie.Status.Should().Be(BiometricEstados.Expirado);
    }

    // ── Prevalidación (módulo Identidad) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Prevalidacion_ZombieDeTramiteAnulado_SeCierraYSeCreaLaPrevalidacion()
    {
        var personRepo = PersonaNatural();
        var zombie = Fila(BiometricEstados.EnProceso, Tramite(TramiteEstado.Anulado));
        StubBloqueos(zombie);

        var (result, error, _) = await PrevalidacionHandler(personRepo).HandleAsync(Tenant, PrevalidacionRequest(), Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        zombie.Status.Should().Be(BiometricEstados.Expirado);
        await _kyverum.Received(1).StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prevalidacion_BloqueoVivoDeUnTramite_Responde409ConEstadoYOrigenSinLlamarAKyverum()
    {
        var personRepo = PersonaNatural();
        var viva = Fila(BiometricEstados.EnProceso, Tramite(TramiteEstado.Borrador));
        StubBloqueos(viva);

        var (result, error, conflict) = await PrevalidacionHandler(personRepo).HandleAsync(Tenant, PrevalidacionRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be("prevalidacion_activa");
        conflict!.Status.Should().Be(BiometricEstados.EnProceso, "el 409 ya no llega sin estado («disponible»)");
        conflict.Origen.Should().Be(IdentitySendOrigen.Tramite, "el frontend indica gestionarla desde el trámite");
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────────────────────────

    private IniciarKyverumVerifyHandler KyverumHandler() =>
        new(_repo, _kyverum, new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(), _audit, _vault);

    private IniciarPrevalidacionHandler PrevalidacionHandler(IPersonRepository personRepo) =>
        new(personRepo, _repo, _kyverum, new BiometricsProviderOptions { Provider = BiometricProviders.Kyverum },
            new FakeWebhookSecretProtector(), Substitute.For<IIdentityValidationEventPublisher>(), _audit);

    private void StubBloqueos(params ProcedureInstanceBiometricValidation[] filas) =>
        _repo.ListInFlightBlockersByDocumentAsync(Tenant, "CC", Documento, Arg.Any<CancellationToken>())
            .Returns(filas);

    private static ProcedureInstance Tramite(string estado, bool borrado = false) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        Status = estado,
        DeletedAt = borrado ? DateTimeOffset.UtcNow.AddDays(-1) : null,
    };

    private static ProcedureInstanceBiometricValidation Fila(string estado, ProcedureInstance? tramite) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        ProcedureInstanceId = tramite?.Id,
        ProcedureInstance = tramite,
        PartyRole = tramite is null ? null : BiometricRules.ParteComprador,
        Name = "Persona de prueba",
        DocumentType = "CC",
        DocumentNumber = Documento,
        Email = "persona@prueba.test",
        Status = estado,
        Provider = BiometricProviders.Kyverum,
        KyverumVerificationId = "kyv_zombie",
        TokenHash = Guid.NewGuid().ToString("N"),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(-20),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-21),
    };

    private ProcedureInstance TramiteBorradorConVendedor()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Traspaso,
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT2-0000181",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = instance.Id,
            ActorType = BiometricRules.ParteVendedor,
            FullName = "Persona de prueba",
            DocumentType = "CC",
            DocumentNumber = Documento,
            PersonType = "natural",
            Email = "persona@prueba.test",
            Ordinal = 1,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private static IPersonRepository PersonaNatural()
    {
        var personRepo = Substitute.For<IPersonRepository>();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = Documento,
            FullName = "Persona de prueba",
            Email = "persona@prueba.test",
            PersonType = PersonTypes.Natural,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        personRepo.FindOrCreateAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(person);
        personRepo.FindActiveStandaloneValidationAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        return personRepo;
    }

    private static IniciarPrevalidacionRequest PrevalidacionRequest() =>
        new("CC", Documento, "Persona de prueba", "persona@prueba.test", PersonTypes.Natural);
}
