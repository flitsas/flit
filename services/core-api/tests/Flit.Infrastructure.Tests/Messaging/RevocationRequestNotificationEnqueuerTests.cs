using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #12572/#12576/#12579 — sink del sub-flujo de revocatoria: bitácora + cola real de correo
/// (ADR-0046 Opción B extendido). Idempotencia verificada sobre la rama InMemory (Postgres UNIQUE
/// queda en el DDL 116 + evidencia DEV, mismo criterio que su gemela).
/// </summary>
public sealed class RevocationRequestNotificationEnqueuerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();
    private static readonly Guid RevocationRequestId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NotifyAsync_InsertaFilaPendienteConMilestoneSolicitada()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedRequesterAsync(dbName, "radicador@flit.test", "Radicador Uno");

        await NewSut(dbName).NotifyAsync(SolicitadaEvent(), Ct);

        await using var verify = NewContext(dbName);
        var rows = await verify.RevocationRequestEmailDispatches.ToListAsync(Ct);
        rows.Should().ContainSingle();
        rows[0].Milestone.Should().Be(RevocationRequestEmailMilestone.Solicitada);
        rows[0].TemplateKey.Should().Be(RevocationRequestNotificationEnqueuer.TemplateKey);
        rows[0].Status.Should().Be("pendiente");
        rows[0].Recipient.Should().Be("radicador@flit.test");
        rows[0].RecipientRole.Should().Be(TramiteNotificationRecipientResolver.RoleRadicador);
        rows[0].DecisionReason.Should().BeNull();

        var events = await verify.ProcedureInstanceEvents.ToListAsync(Ct);
        events.Should().ContainSingle(e => e.Tipo == RevocationRequestNotificationEnqueuer.EventoTipo);
    }

    [Fact]
    public async Task NotifyAsync_Reintento_NoEncolaFilaDuplicada()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedRequesterAsync(dbName, "radicador@flit.test", "Radicador Uno");

        var sut = NewSut(dbName);
        var evt = SolicitadaEvent();
        await sut.NotifyAsync(evt, Ct);
        await NewSut(dbName).NotifyAsync(evt, Ct);

        await using var verify = NewContext(dbName);
        var rows = await verify.RevocationRequestEmailDispatches.ToListAsync(Ct);
        rows.Should().ContainSingle();
    }

    [Fact]
    public async Task NotifyAsync_SinCorreoResoluble_NoEncolaFilaPeroSiBitacora()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        // Sin seed de requester: el radicador no se resuelve.

        await NewSut(dbName).NotifyAsync(SolicitadaEvent(), Ct);

        await using var verify = NewContext(dbName);
        (await verify.RevocationRequestEmailDispatches.CountAsync(Ct)).Should().Be(0);
        var events = await verify.ProcedureInstanceEvents.ToListAsync(Ct);
        events.Should().ContainSingle(e => e.Tipo == RevocationRequestNotificationEnqueuer.EventoTipo);
    }

    [Fact]
    public async Task NotifyDecisionAsync_Aprobada_InsertaFilaConMilestoneAprobadaSinMotivo()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedRequesterAsync(dbName, "radicador@flit.test", "Radicador Uno");

        await NewSut(dbName).NotifyDecisionAsync(DecisionEvent(approved: true), Ct);

        await using var verify = NewContext(dbName);
        var rows = await verify.RevocationRequestEmailDispatches.ToListAsync(Ct);
        rows.Should().ContainSingle();
        rows[0].Milestone.Should().Be(RevocationRequestEmailMilestone.Aprobada);
        rows[0].TemplateKey.Should().Be(RevocationRequestNotificationEnqueuer.DecisionTemplateKeyAprobada);
        rows[0].DecisionReason.Should().BeNull();

        var events = await verify.ProcedureInstanceEvents.ToListAsync(Ct);
        events.Should().ContainSingle(e => e.Tipo == RevocationRequestNotificationEnqueuer.DecisionEventoTipo);
    }

    [Fact]
    public async Task NotifyDecisionAsync_Rechazada_DenormalizaMotivoDesdeLaSolicitud()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedRequesterAsync(dbName, "radicador@flit.test", "Radicador Uno");
        await SeedRevocationRequestAsync(dbName, decisionReason: "El soporte adjunto no es legible.");

        await NewSut(dbName).NotifyDecisionAsync(DecisionEvent(approved: false), Ct);

        await using var verify = NewContext(dbName);
        var rows = await verify.RevocationRequestEmailDispatches.ToListAsync(Ct);
        rows.Should().ContainSingle();
        rows[0].Milestone.Should().Be(RevocationRequestEmailMilestone.Rechazada);
        rows[0].TemplateKey.Should().Be(RevocationRequestNotificationEnqueuer.DecisionTemplateKeyRechazada);
        rows[0].DecisionReason.Should().Be("El soporte adjunto no es legible.");
    }

    [Fact]
    public void BuildDispatchRows_CupoOmitido_QuedaMarcadoSinDuplicarNadaMas()
    {
        var resolution = new TramiteRecipientResolution(
            [],
            [new TramiteRecipientGap(TramiteNotificationRecipientResolver.RoleRadicador, TramiteRecipientKind.Persona, "Sin Mail")]);

        var rows = RevocationRequestNotificationEnqueuer.BuildDispatchRows(
            TenantId, InstanceId, RevocationRequestId, attemptNumber: 1,
            RevocationRequestEmailMilestone.Solicitada, RevocationRequestNotificationEnqueuer.TemplateKey,
            resolution, decisionReason: null, createdBy: RequesterId,
            NullLogger<RevocationRequestNotificationEnqueuer>.Instance);

        rows.Should().ContainSingle();
        rows[0].Status.Should().Be("omitido");
        rows[0].Recipient.Should().BeNull();
        rows[0].FailureReason.Should().Contain("persona");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string NewDbName() => $"flit-revocation-email-enqueue-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static RevocationRequestNotificationEnqueuer NewSut(string dbName)
    {
        var db = NewContext(dbName);
        return new RevocationRequestNotificationEnqueuer(
            db,
            new TramiteNotificationRecipientResolver(),
            NullLogger<RevocationRequestNotificationEnqueuer>.Instance);
    }

    private static RevocationRequestSolicitadaEvent SolicitadaEvent() => new(
        TenantId, InstanceId, RevocationRequestId, AttemptNumber: 1, RequesterId, DateTimeOffset.UtcNow);

    private static RevocationRequestDecidedEvent DecisionEvent(bool approved) => new(
        TenantId, InstanceId, RevocationRequestId, AttemptNumber: 1, approved, RequesterId,
        DecidedBy: Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static async Task SeedInstanceAsync(string dbName)
    {
        await using var db = NewContext(dbName);
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "REV-1",
            Status = "aprobado",
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedRequesterAsync(string dbName, string email, string displayName)
    {
        await using var db = NewContext(dbName);
        db.Users.Add(new User
        {
            Id = RequesterId,
            Email = email,
            DisplayName = displayName,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedRevocationRequestAsync(string dbName, string decisionReason)
    {
        await using var db = NewContext(dbName);
        db.ProcedureRevocationRequests.Add(new ProcedureRevocationRequest
        {
            Id = RevocationRequestId,
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            AttemptNumber = 1,
            Status = ProcedureRevocationRequestStatus.Rechazada,
            RequestedBy = RequesterId,
            RequestedAt = DateTimeOffset.UtcNow,
            DecidedBy = Guid.NewGuid(),
            DecidedAt = DateTimeOffset.UtcNow,
            DecisionReason = decisionReason,
        });
        await db.SaveChangesAsync(Ct);
    }
}
