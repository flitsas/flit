using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ParticipantHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly InvitarParticipanteHandler _invitar;
    private readonly ListParticipantesHandler _list;
    private readonly ReinvitarParticipanteHandler _reinvitar;

    public ParticipantHandlerTests()
    {
        _invitar = new InvitarParticipanteHandler(_repo);
        _list = new ListParticipantesHandler(_repo);
        _reinvitar = new ReinvitarParticipanteHandler(_repo);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "traspaso",
            TipologiaCodigo = "traspaso_standard",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static InvitarParticipanteInput Input(string rol = "comprador") =>
        new(rol, "Juan Perez", "juan@x.com", "3001234567", WhatsappOptIn: true);

    // ── Invitar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invitar_HappyPath_ReturnsRawTokenAndMagicLinkPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _invitar.HandleAsync(id, tenant, Input(), null, ct);

        error.Should().BeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.MagicLinkPath.Should().Be($"/portal/{result.Token}");
        result.Participant.Rol.Should().Be("comprador");
        result.Participant.WhatsappOptIn.Should().BeTrue();
        instance.Participants.Should().ContainSingle();
        // SEGURIDAD: el hash persistido NO es el token crudo; es su SHA-256.
        var persisted = instance.Participants.Single();
        persisted.TokenHash.Should().NotBe(result.Token);
        persisted.TokenHash.Should().Be(PortalToken.Hash(result.Token));
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceParticipant>());
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "participante_invitado"), ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Invitar_TokenLookupByHash_Matches()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (result, _) = await _invitar.HandleAsync(id, tenant, Input(), null, ct);

        // El lookup público es por hash del token crudo → debe coincidir con lo persistido.
        var byHash = PortalToken.Hash(result!.Token);
        instance.Participants.Single().TokenHash.Should().Be(byHash);
    }

    [Fact]
    public async Task Invitar_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithParticipantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _invitar.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Input(), null, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Invitar_InvalidRol_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _invitar.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Input(rol: "tercero"), null, ct);

        error.Should().Be("rol_invalido");
    }

    [Fact]
    public async Task Invitar_IncompleteData_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _invitar.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new InvitarParticipanteInput("comprador", "", "", null, false), null, ct);

        error.Should().Be("datos_incompletos");
    }

    [Fact]
    public async Task Invitar_ActiveParticipantSameRol_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Participants.Add(new ProcedureInstanceParticipant
        {
            Id = Guid.NewGuid(),
            Rol = "comprador",
            Nombre = "Otro",
            Email = "o@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _invitar.HandleAsync(id, tenant, Input(), null, ct);

        error.Should().Be("participante_activo");
    }

    [Fact]
    public async Task Invitar_CompletedSameRol_AllowsNewInvite()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Participants.Add(new ProcedureInstanceParticipant
        {
            Id = Guid.NewGuid(),
            Rol = "comprador",
            Nombre = "Otro",
            Email = "o@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _invitar.HandleAsync(id, tenant, Input(), null, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithParticipantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _list.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task List_ReturnsParticipants()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Participants.Add(new ProcedureInstanceParticipant
        {
            Id = Guid.NewGuid(),
            Rol = "vendedor",
            Nombre = "V",
            Email = "v@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _list.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Participants.Should().ContainSingle().Which.Rol.Should().Be("vendedor");
    }

    // ── Reinvitar ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reinvitar_RotatesTokenAndResetsExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        var oldHash = PortalToken.Hash("old-token");
        var pid = Guid.NewGuid();
        instance.Participants.Add(new ProcedureInstanceParticipant
        {
            Id = pid,
            Rol = "comprador",
            Nombre = "J",
            Email = "j@x.com",
            TokenHash = oldHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _reinvitar.HandleAsync(id, tenant, pid, ct);

        error.Should().BeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        var p = instance.Participants.Single();
        p.TokenHash.Should().NotBe(oldHash);
        p.TokenHash.Should().Be(PortalToken.Hash(result.Token));
        p.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        p.LastReminderAt.Should().NotBeNull();
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "participante_reinvitado"), ct);
    }

    [Fact]
    public async Task Reinvitar_WithinCooldown_Returns429()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        var pid = Guid.NewGuid();
        instance.Participants.Add(new ProcedureInstanceParticipant
        {
            Id = pid,
            Rol = "comprador",
            Nombre = "J",
            Email = "j@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            LastReminderAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _reinvitar.HandleAsync(id, tenant, pid, ct);

        error.Should().Be("reminder_cooldown");
    }

    [Fact]
    public async Task Reinvitar_AlreadyCompleted_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        var pid = Guid.NewGuid();
        instance.Participants.Add(new ProcedureInstanceParticipant
        {
            Id = pid,
            Rol = "comprador",
            Nombre = "J",
            Email = "j@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CompletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithParticipantsAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _reinvitar.HandleAsync(id, tenant, pid, ct);

        error.Should().Be("ya_completado");
    }
}
