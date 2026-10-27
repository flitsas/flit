using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13821 (Épica #13817) — reenvío a una o varias personas con correo sincronizado.
/// AC3: varias personas en una solicitud. AC4: el correo nuevo queda en la validación Y en el actor del
/// trámite (o en su representante legal). AC5: el tracking guarda correo anterior y nuevo. AC6: el fallo de
/// una persona no frena a las demás; el trámite cerrado rechaza toda la solicitud.
/// </summary>
public sealed class AdminReenviarValidacionesLoteHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private AdminReenviarValidacionIdentidadHandler BuildSingle() => new(
        _repo, _kyverum, new BiometricsProviderOptions { Provider = BiometricProviders.Mock },
        new FakeWebhookSecretProtector(), _events, _audit);

    private AdminReenviarValidacionesLoteHandler BuildLote() => new(BuildSingle(), _repo);

    private ProcedureInstance Instance(string status = "borrador")
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013821",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdAsync(instance.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithActorsAsync(instance.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private ProcedureInstanceActor ActorNatural(ProcedureInstance instance, string parte, string documento, string email, int ordinal = 1)
    {
        var actor = new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instance.Id,
            ActorType = parte,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = $"PERSONA {documento}",
            Email = email,
            PersonType = "natural",
            Ordinal = ordinal,
        };
        instance.Actors.Add(actor);
        return actor;
    }

    private ProcedureInstanceBiometricValidation Validacion(
        ProcedureInstance instance, string parte, string documento, string email, string status = BiometricEstados.Enviado)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = parte,
            Name = $"PERSONA {documento}",
            DocumentType = "CC",
            DocumentNumber = documento,
            Email = email,
            RegisteredEmail = email,
            Status = status,
            Provider = BiometricProviders.Mock,
            TokenHash = "old-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _repo.GetBiometricByIdWithPersonAsync(v.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    // ── AC4 — correo sincronizado ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_PersonaNatural_ElCorreoNuevoQuedaEnLaValidacionYEnElActor()
    {
        var instance = Instance();
        var actor = ActorNatural(instance, "vendedor", "222", "a@x.com");
        var v = Validacion(instance, "vendedor", "222", "a@x.com");

        var (result, error, _, _) = await BuildSingle().HandleAsync(
            new AdminReenviarValidacionIdentidadCommand(instance.Id, v.Id, _tenantId, "b@x.com", null),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.EmailActualizado.Should().BeTrue();
        v.Email.Should().Be("b@x.com");
        actor.Email.Should().Be("b@x.com");
    }

    [Fact]
    public async Task AC4_PersonaJuridica_SeActualizaElCorreoDelRepresentanteLegalNoElDeLaEmpresa()
    {
        var instance = Instance();
        var actor = new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instance.Id,
            ActorType = "vendedor",
            DocumentType = "NIT",
            DocumentNumber = "900511343",
            FullName = "EMPRESA S.A.S.",
            Email = "empresa@x.com",
            PersonType = "juridical",
            Metadata = ActorMetadataReader.Serialize("BOGOTA", "CALLE 1",
                new ActorRepresentanteLegal("CC", "79522832", "REPRESENTANTE", "rl.viejo@x.com", "3000000000")),
        };
        instance.Actors.Add(actor);
        var v = Validacion(instance, "vendedor", "79522832", "rl.viejo@x.com");

        var (_, error, _, _) = await BuildSingle().HandleAsync(
            new AdminReenviarValidacionIdentidadCommand(instance.Id, v.Id, _tenantId, "rl.nuevo@x.com", null),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        actor.Email.Should().Be("empresa@x.com", "el correo de la empresa no es el del sujeto de identidad");
        var (ciudad, direccion, rl, _) = ActorMetadataReader.Parse(actor.Metadata);
        rl!.Email.Should().Be("rl.nuevo@x.com");
        rl.Telefono.Should().Be("3000000000");
        ciudad.Should().Be("BOGOTA");
        direccion.Should().Be("CALLE 1");
    }

    [Fact]
    public async Task AC4_Copropietarios_SoloCambiaElCorreoDeLaPersonaReenviada()
    {
        var instance = Instance();
        var ana = ActorNatural(instance, "vendedor", "221", "ana@x.com", ordinal: 1);
        var luis = ActorNatural(instance, "vendedor", "222", "luis@x.com", ordinal: 2);
        var v = Validacion(instance, "vendedor", "222", "luis@x.com");

        await BuildSingle().HandleAsync(
            new AdminReenviarValidacionIdentidadCommand(instance.Id, v.Id, _tenantId, "luis.nuevo@x.com", null),
            TestContext.Current.CancellationToken);

        luis.Email.Should().Be("luis.nuevo@x.com");
        ana.Email.Should().Be("ana@x.com");
    }

    [Fact]
    public async Task AC4_SinCambioDeCorreo_NoTocaElActor()
    {
        var instance = Instance();
        var actor = ActorNatural(instance, "comprador", "111", "actor@x.com");
        var v = Validacion(instance, "comprador", "111", "validacion@x.com");

        await BuildSingle().HandleAsync(
            new AdminReenviarValidacionIdentidadCommand(instance.Id, v.Id, _tenantId, null, null),
            TestContext.Current.CancellationToken);

        actor.Email.Should().Be("actor@x.com");
    }

    // ── AC5 — tracking con correo anterior y nuevo ──────────────────────────────────────────────────

    [Fact]
    public async Task AC5_ElTrackingGuardaCorreoAnteriorYNuevo()
    {
        var instance = Instance();
        ActorNatural(instance, "vendedor", "222", "a@x.com");
        var v = Validacion(instance, "vendedor", "222", "a@x.com");
        var userId = Guid.NewGuid();
        ProcedureInstanceEvent? evento = null;
        await _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => evento = e), Arg.Any<CancellationToken>());

        await BuildSingle().HandleAsync(
            new AdminReenviarValidacionIdentidadCommand(instance.Id, v.Id, _tenantId, "b@x.com", userId),
            TestContext.Current.CancellationToken);

        evento.Should().NotBeNull();
        evento!.CreatedBy.Should().Be(userId);
        using var payload = JsonDocument.Parse(evento.Payload!);
        payload.RootElement.GetProperty("party_role").GetString().Should().Be("vendedor");
        payload.RootElement.GetProperty("correo_anterior").GetString().Should().Be("a@x.com");
        payload.RootElement.GetProperty("correo_destino").GetString().Should().Be("b@x.com");
        payload.RootElement.GetProperty("actor_actualizado").GetBoolean().Should().BeTrue();
    }

    // ── AC3 / AC6 — reenvío múltiple ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_DosPersonas_SeReenviaACadaUnaConSuResultado()
    {
        var instance = Instance();
        ActorNatural(instance, "comprador", "111", "c@x.com");
        ActorNatural(instance, "vendedor", "222", "v@x.com");
        var comprador = Validacion(instance, "comprador", "111", "c@x.com");
        var vendedor = Validacion(instance, "vendedor", "222", "v@x.com");

        var (result, error, _) = await BuildLote().HandleAsync(
            new AdminReenviarValidacionesLoteCommand(instance.Id, _tenantId,
                [new(comprador.Id, null), new(vendedor.Id, "v.nuevo@x.com")], null),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.Error == null && i.Result != null);
        result.Items.Single(i => i.ValidationId == vendedor.Id).Result!.EmailActualizado.Should().BeTrue();
        await _repo.Received(2).AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_UnaAprobada_NoFrenaALaOtra()
    {
        var instance = Instance();
        var aprobada = Validacion(instance, "comprador", "111", "c@x.com", BiometricEstados.Aprobado);
        var pendiente = Validacion(instance, "vendedor", "222", "v@x.com");

        var (result, error, _) = await BuildLote().HandleAsync(
            new AdminReenviarValidacionesLoteCommand(instance.Id, _tenantId,
                [new(aprobada.Id, null), new(pendiente.Id, null)], null),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Items.Single(i => i.ValidationId == aprobada.Id).Error
            .Should().Be(AdminReenviarValidacionIdentidadHandler.IdentidadAprobada);
        result.Items.Single(i => i.ValidationId == pendiente.Id).Error.Should().BeNull();
    }

    [Fact]
    public async Task AC6_EntreUnaPersonaYOtra_SeLimpiaElRastreoDeEf()
    {
        var instance = Instance();
        var a = Validacion(instance, "comprador", "111", "c@x.com");
        var b = Validacion(instance, "vendedor", "222", "v@x.com");

        await BuildLote().HandleAsync(
            new AdminReenviarValidacionesLoteCommand(instance.Id, _tenantId, [new(a.Id, null), new(b.Id, null)], null),
            TestContext.Current.CancellationToken);

        _repo.Received(1).ResetTracking();
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData("revocado")]
    public async Task AC6_TramiteCerrado_RechazaTodaLaSolicitud(string estado)
    {
        var instance = Instance(estado);
        var a = Validacion(instance, "comprador", "111", "c@x.com");
        var b = Validacion(instance, "vendedor", "222", "v@x.com");

        var (result, error, _) = await BuildLote().HandleAsync(
            new AdminReenviarValidacionesLoteCommand(instance.Id, _tenantId, [new(a.Id, null), new(b.Id, null)], null),
            TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.IdentidadReenvioNoDisponible);
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TramiteInexistenteODeOtroTenant_RechazaLaSolicitudSinTocarValidaciones()
    {
        var (result, error, _) = await BuildLote().HandleAsync(
            new AdminReenviarValidacionesLoteCommand(Guid.NewGuid(), _tenantId, [new(Guid.NewGuid(), null)], null),
            TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        await _repo.DidNotReceive().GetBiometricByIdWithPersonAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SolicitudVacia_SeRechaza()
    {
        var (result, error, _) = await BuildLote().HandleAsync(
            new AdminReenviarValidacionesLoteCommand(Guid.NewGuid(), _tenantId, [], null),
            TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be(AdminReenviarValidacionesLoteHandler.SolicitudVacia);
    }

    [Fact]
    public async Task MasDeOchoPersonas_SeRechaza()
    {
        var items = Enumerable.Range(0, AdminReenviarValidacionesLoteHandler.MaxItems + 1)
            .Select(_ => new AdminReenviarValidacionLoteItem(Guid.NewGuid(), null))
            .ToList();

        var (_, error, _) = await BuildLote().HandleAsync(
            new AdminReenviarValidacionesLoteCommand(Guid.NewGuid(), _tenantId, items, null),
            TestContext.Current.CancellationToken);

        error.Should().Be(AdminReenviarValidacionesLoteHandler.DemasiadasPersonas);
    }
}
