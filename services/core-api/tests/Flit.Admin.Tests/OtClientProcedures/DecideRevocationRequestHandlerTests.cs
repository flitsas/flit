using Flit.Admin.Application.OtClientProcedures.RevokeOtClientProcedure;
using Flit.Admin.Application.OtProfile;
using Flit.Admin.Domain.OtProfile;
using Flit.Api.UseCases.RevocationRequests;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #12576 (Feature #12565) — «Decisión OT de una solicitud de revocatoria»: AC1 (aprobar, reutiliza
/// el handler de HU #12166), AC2 (rechazar con motivo obligatorio, el trámite permanece Aprobado), AC3
/// (autorización — la resuelve la policy del endpoint, no este handler; ver
/// <c>AdminOtRevocationRequestAuthorizationTests</c>) y AC4 (notificación best-effort de la decisión).
///
/// <para>
/// Mismo patrón que <see cref="OtClientProcedureHandlerTests"/>/<see cref="OtClientProcedureQuipuxReadOnlyTests"/>:
/// EF Core InMemory con los repositorios REALES de Infrastructure (no dobles) — <c>IOtClientProcedureRepository</c>
/// hace demasiado (resolución de grant, scope RLS simulado) para sustituirlo con NSubstitute sin reescribir
/// su comportamiento. Con el proveedor InMemory, <c>ExecuteInClientTenantScopeAsync</c>/<c>ExecuteOtScopedAsync</c>
/// se saltan la transacción real (<c>IsRelational() == false</c>) y ejecutan la acción directo.
/// </para>
/// </summary>
public sealed class DecideRevocationRequestHandlerTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Requester = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtDecisor = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── AC1 — aprobar ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ProcedureRevocationRequestStatus.Solicitada)]
    [InlineData(ProcedureRevocationRequestStatus.EnRevision)]
    public async Task AC1_Aprobar_SolicitudActivaSolicitadaOEnRevision_RevocaElTramiteYAceptaLaSolicitud(string origenStatus)
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedGrant(seed);
            SeedActorUser(seed, OtDecisor);
            SeedActorUser(seed, Requester);
            SeedProcedure(seed, procedureId, TramiteEstado.Aprobado, plate: "ABC123");
            SeedRevocationRequest(seed, requestId, procedureId, origenStatus, attemptNumber: 1);
        }

        await using var ctx = NewContext(db);
        var notifier = Substitute.For<IRevocationRequestNotifier>();
        var handler = NewHandler(ctx, notifier, new AllowAllQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: true, Reason: null, DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.Approved);
        result.Procedure!.Status.Should().Be(TramiteEstado.Revocado);
        result.RevocationRequestId.Should().Be(requestId);
        result.RequestStatus.Should().Be(ProcedureRevocationRequestStatus.Aprobada);

        await using var verify = NewContext(db);
        var procedure = await verify.ProcedureInstances.SingleAsync(
            p => p.Id == procedureId, TestContext.Current.CancellationToken);
        procedure.Status.Should().Be(TramiteEstado.Revocado, "AC1 reutiliza el handler de HU #12166");

        var request = await verify.ProcedureRevocationRequests.SingleAsync(
            r => r.Id == requestId, TestContext.Current.CancellationToken);
        request.Status.Should().Be(ProcedureRevocationRequestStatus.Aprobada);
        request.DecidedBy.Should().Be(OtDecisor);
        request.DecidedAt.Should().NotBeNull();

        // AC4 — notificación de la decisión, best-effort.
        await notifier.Received(1).NotifyDecisionAsync(
            Arg.Is<RevocationRequestDecidedEvent>(e =>
                e.RevocationRequestId == requestId
                && e.ProcedureInstanceId == procedureId
                && e.Approved
                && e.RequestedByUserId == Requester
                && e.DecidedBy == OtDecisor),
            Arg.Any<CancellationToken>());

        // Feature #12565 (HU #12577) — tracking: la aprobación deja un evento propio, no solo la
        // transición genérica de estado, para que el timeline muestre motivo/decisor de la decisión.
        var evt = await verify.ProcedureInstanceEvents.SingleAsync(
            e => e.ProcedureInstanceId == procedureId && e.Tipo == DecideRevocationRequestHandler.EventoTipoAprobada,
            TestContext.Current.CancellationToken);
        evt.CreatedBy.Should().Be(OtDecisor);
        evt.Payload.Should().Contain(requestId.ToString()).And.Contain("\"attempt_number\":1");
    }

    [Fact]
    public async Task AC1_Aprobar_LiberaLaPlacaYMarcaAdjuntosHistoricos_MismoEfectoQueHU12166()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedGrant(seed);
            SeedActorUser(seed, OtDecisor);
            SeedActorUser(seed, Requester);
            SeedProcedure(seed, procedureId, TramiteEstado.Aprobado, plate: "ABC123");
            SeedRevocationRequest(seed, requestId, procedureId, ProcedureRevocationRequestStatus.EnRevision, attemptNumber: 1);
            seed.ProcedureInstanceAttachments.Add(new ProcedureInstanceAttachment
            {
                Id = attachmentId,
                TenantId = ClientTenant,
                ProcedureInstanceId = procedureId,
                Tipo = "fur",
                Filename = "fur.pdf",
                Mimetype = "application/pdf",
                SizeBytes = 1,
                Sha256 = "x",
                StoragePath = "/x",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = NewHandler(ctx, Substitute.For<IRevocationRequestNotifier>(), new AllowAllQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: true, Reason: null, DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.Approved);

        await using var verify = NewContext(db);
        var attachment = await verify.ProcedureInstanceAttachments.SingleAsync(
            a => a.Id == attachmentId, TestContext.Current.CancellationToken);
        attachment.IsHistorico.Should().BeTrue();
        TramiteEstado.OcupaPlaca(TramiteEstado.Revocado).Should().BeFalse();
    }

    // ── AC2 — rechazar ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Rechazar_SinMotivo_Retorna422MotivoRequeridoYNoTocaLaSolicitud()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedGrant(seed);
            SeedActorUser(seed, Requester);
            SeedProcedure(seed, procedureId, TramiteEstado.Aprobado);
            SeedRevocationRequest(seed, requestId, procedureId, ProcedureRevocationRequestStatus.Solicitada, attemptNumber: 1);
        }

        await using var ctx = NewContext(db);
        var notifier = Substitute.For<IRevocationRequestNotifier>();
        var handler = NewHandler(ctx, notifier, new AllowAllQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: false, Reason: "   ", DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.MotivoRequerido);

        await using var verify = NewContext(db);
        var request = await verify.ProcedureRevocationRequests.SingleAsync(
            r => r.Id == requestId, TestContext.Current.CancellationToken);
        request.Status.Should().Be(ProcedureRevocationRequestStatus.Solicitada, "el fail-fast no debe tocar BD");
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Aprobado);

        await notifier.DidNotReceive().NotifyDecisionAsync(Arg.Any<RevocationRequestDecidedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Rechazar_ConMotivo_DejaLaSolicitudRechazadaYElTramiteSigueAprobado_HabilitandoReintento()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedGrant(seed);
            SeedActorUser(seed, OtDecisor);
            SeedActorUser(seed, Requester);
            SeedProcedure(seed, procedureId, TramiteEstado.Aprobado);
            SeedRevocationRequest(seed, requestId, procedureId, ProcedureRevocationRequestStatus.EnRevision, attemptNumber: 1);
        }

        await using var ctx = NewContext(db);
        var notifier = Substitute.For<IRevocationRequestNotifier>();
        var handler = NewHandler(ctx, notifier, new AllowAllQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: false, Reason: "Falta soporte suficiente para revocar.", DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.Rejected);
        result.RequestStatus.Should().Be(ProcedureRevocationRequestStatus.Rechazada);

        await using var verify = NewContext(db);
        var request = await verify.ProcedureRevocationRequests.SingleAsync(
            r => r.Id == requestId, TestContext.Current.CancellationToken);
        request.Status.Should().Be(ProcedureRevocationRequestStatus.Rechazada);
        request.DecidedBy.Should().Be(OtDecisor);
        request.DecisionReason.Should().Be("Falta soporte suficiente para revocar.");

        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Aprobado, "el trámite permanece Aprobado (ADR-0022)");

        // Habilita reintento — ya no hay solicitud ACTIVA para el trámite (AC5 de HU #12571, wiring).
        var revocationRepo = new ProcedureRevocationRequestRepository(verify);
        (await revocationRepo.FindActiveAsync(ClientTenant, procedureId, TestContext.Current.CancellationToken))
            .Should().BeNull();
        (await revocationRepo.GetNextAttemptNumberAsync(ClientTenant, procedureId, TestContext.Current.CancellationToken))
            .Should().Be(2);

        await notifier.Received(1).NotifyDecisionAsync(
            Arg.Is<RevocationRequestDecidedEvent>(e => e.RevocationRequestId == requestId && !e.Approved),
            Arg.Any<CancellationToken>());

        // Feature #12565 (HU #12577) — tracking: el rechazo también deja un evento propio, no solo el
        // "Solicitud de revocatoria" original (ese intento nunca cambia de estado, así que sin este
        // evento un rechazo no dejaba NINGÚN rastro en el timeline).
        var evt = await verify.ProcedureInstanceEvents.SingleAsync(
            e => e.ProcedureInstanceId == procedureId && e.Tipo == DecideRevocationRequestHandler.EventoTipoRechazada,
            TestContext.Current.CancellationToken);
        evt.CreatedBy.Should().Be(OtDecisor);
        evt.Payload.Should().Contain(requestId.ToString()).And.Contain("Falta soporte suficiente para revocar.");
    }

    // ── Casos límite compartidos ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinSolicitudActiva_Retorna404RequestNotFound_YNoRevocaElTramite()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedGrant(seed);
            SeedProcedure(seed, procedureId, TramiteEstado.Aprobado);
        }

        await using var ctx = NewContext(db);
        var handler = NewHandler(ctx, Substitute.For<IRevocationRequestNotifier>(), new AllowAllQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: true, Reason: null, DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.RequestNotFound);

        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Aprobado);
    }

    [Fact]
    public async Task TramiteNoAccesibleParaElOt_Retorna404ProcedureNotFound()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            // Sin perfil OT (SeedOt) para OtTenant: ResolveTransitOfficeIdAsync no resuelve organismo,
            // así que ningún trámite es accesible (mismo comportamiento que approve/reject/revoke).
            SeedProcedure(seed, procedureId, TramiteEstado.Aprobado);
        }

        await using var ctx = NewContext(db);
        var handler = NewHandler(ctx, Substitute.For<IRevocationRequestNotifier>(), new AllowAllQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: true, Reason: null, DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.ProcedureNotFound);
    }

    [Fact]
    public async Task Aprobar_ConGuardQuipuxBloqueado_Retorna403SinTocarLaSolicitud()
    {
        // NOTA: RevokeOtClientProcedureHandler valida el guard con la acción "revocar", que
        // QuipuxReadOnlyGuard.RestrictedActions (HU #10215) NO incluye (solo aprobar/rechazar/
        // generar_consolidado/adjuntar_lt) — mismo comportamiento PRE-EXISTENTE de HU #12166, fuera de
        // alcance de esta HU. Este test prueba el WIRING propio (que este handler propague
        // QuipuxReadOnly sin tocar la solicitud) con un guard doble que SÍ bloquea, no la matriz de
        // acciones restringidas de QuipuxReadOnlyGuard (ya cubierta por sus propios tests).
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedGrant(seed);
            SeedProcedure(seed, procedureId, TramiteEstado.Aprobado);
            SeedRevocationRequest(seed, requestId, procedureId, ProcedureRevocationRequestStatus.Solicitada, attemptNumber: 1);
        }

        await using var ctx = NewContext(db);
        var handler = NewHandler(ctx, Substitute.For<IRevocationRequestNotifier>(), new AlwaysBlockedQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: true, Reason: null, DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.QuipuxReadOnly);

        await using var verify = NewContext(db);
        var request = await verify.ProcedureRevocationRequests.SingleAsync(
            r => r.Id == requestId, TestContext.Current.CancellationToken);
        request.Status.Should().Be(ProcedureRevocationRequestStatus.Solicitada, "no se decide nada en modo QX read-only");
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Aprobado);
    }

    [Fact]
    public async Task Aprobar_TramiteYaNoAprobado_Retorna409InvalidStateYNoAceptaLaSolicitud()
    {
        // Carrera: la solicitud quedó activa, pero el trámite dejó de estar 'aprobado' por otra vía
        // antes de que el OT decidiera. La solicitud NO debe quedar 'aprobada' sin que el trámite se
        // haya revocado de verdad (mentiría sobre una revocación que no ocurrió).
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedGrant(seed);
            SeedProcedure(seed, procedureId, TramiteEstado.Entregado);
            SeedRevocationRequest(seed, requestId, procedureId, ProcedureRevocationRequestStatus.Solicitada, attemptNumber: 1);
        }

        await using var ctx = NewContext(db);
        var handler = NewHandler(ctx, Substitute.For<IRevocationRequestNotifier>(), new AllowAllQuipuxGuard());

        var result = await handler.HandleAsync(new DecideRevocationRequestCommand(
            OtTenant, procedureId, Approve: true, Reason: null, DecidedBy: OtDecisor, TransitOfficeId: null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(DecideRevocationRequestStatus.InvalidState);

        await using var verify = NewContext(db);
        var request = await verify.ProcedureRevocationRequests.SingleAsync(
            r => r.Id == requestId, TestContext.Current.CancellationToken);
        request.Status.Should().Be(ProcedureRevocationRequestStatus.Solicitada);
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────────────────────────

    private static DecideRevocationRequestHandler NewHandler(
        FlitDbContext ctx, IRevocationRequestNotifier notifier, IQuipuxReadOnlyGuard quipuxGuard)
    {
        var otRepo = new OtClientProcedureRepository(ctx, new Flit.Tramites.Domain.Tramites.Estados.NullTramiteTransitionPublisher());
        var revokeHandler = new RevokeOtClientProcedureHandler(otRepo, quipuxGuard);
        var revocationRepo = new ProcedureRevocationRequestRepository(ctx);
        var instanceRepo = new ProcedureInstanceRepository(ctx);
        return new DecideRevocationRequestHandler(
            otRepo, revokeHandler, revocationRepo, instanceRepo, notifier, NullLogger<DecideRevocationRequestHandler>.Instance);
    }

    private static void SeedOt(FlitDbContext ctx)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedGrant(FlitDbContext ctx)
    {
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedActorUser(FlitDbContext ctx, Guid userId)
    {
        if (ctx.Users.Any(u => u.Id == userId))
        {
            return;
        }

        ctx.Users.Add(new User
        {
            Id = userId,
            Email = $"actor-{userId:N}@test.local",
            DisplayName = "Actor Test",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedProcedure(FlitDbContext ctx, Guid id, string status, string? plate = null)
    {
        if (!ctx.ProcedureTypes.Local.Any(pt => pt.Id == ProcedureType) && !ctx.ProcedureTypes.Any(pt => pt.Id == ProcedureType))
        {
            ctx.ProcedureTypes.Add(new Flit.Tramites.Domain.Entities.ProcedureType
            {
                Id = ProcedureType,
                Code = "MATRICULA_NUEVA",
                Name = "Matrícula inicial",
                Family = "MATRICULAS",
                IsActive = true,
                PublicationStatus = Flit.Tramites.Domain.Enums.PublicationStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = "REF-001",
            Status = status,
            Plate = plate,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Requester,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedRevocationRequest(
        FlitDbContext ctx, Guid id, Guid procedureInstanceId, string status, int attemptNumber)
    {
        ctx.ProcedureRevocationRequests.Add(new ProcedureRevocationRequest
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureInstanceId = procedureInstanceId,
            AttemptNumber = attemptNumber,
            Status = status,
            Reason = "El trámite se aprobó con datos incorrectos.",
            RequestedBy = Requester,
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private sealed class AllowAllQuipuxGuard : IQuipuxReadOnlyGuard
    {
        public Task<QuipuxReadOnlyResult> ValidateActionAsync(
            Guid tenantId, string action, CancellationToken cancellationToken = default) =>
            Task.FromResult(QuipuxReadOnlyResult.Allowed());
    }

    private sealed class AlwaysBlockedQuipuxGuard : IQuipuxReadOnlyGuard
    {
        public Task<QuipuxReadOnlyResult> ValidateActionAsync(
            Guid tenantId, string action, CancellationToken cancellationToken = default) =>
            Task.FromResult(QuipuxReadOnlyResult.Forbidden());
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
