using Flit.Admin.Application.OtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>Tests tenant admin OT — trámites de clientes (HU #10217) AC1–AC5.</summary>
public sealed class OtClientProcedureHandlerTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOtTenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid UnlinkedClient = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OtherOffice = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid ProcedureTypeA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProcedureTypeB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ActorUser = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Approver = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task AC1_ListPendingOt_ReturnsOnlyGrantedClientProcedures()
    {
        var db = NewDbName();
        var pendingId = Guid.NewGuid();
        var otherClientProcedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice, isEnabled: true);
            SeedGrant(seed, UnlinkedClient, OtherOffice, isEnabled: true);
            SeedCatalog(seed, ClientTenant, ProcedureTypeA, "Flota Andina S.A.S.", "Matrícula inicial");
            SeedProcedure(seed, pendingId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
            SeedProcedure(seed, otherClientProcedureId, UnlinkedClient, OtherOffice, ProcedureTypeA, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Status = TramiteEstado.Entregado,
        }, TestContext.Current.CancellationToken);

        result.Data.Should().ContainSingle();
        result.Data[0].ProcedureTypeName.Should().Be("Matrícula inicial");
        result.Data[0].ClientTenantName.Should().Be("Flota Andina S.A.S.");
        result.Data[0].Id.Should().Be(pendingId);
        result.Data[0].ClientTenantId.Should().Be(ClientTenant);
    }

    [Fact]
    public async Task AC2_Approve_PersistsApprovedOtAndAuditTrail()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var handler = NewApproveHandler(ctx);
        var result = await handler.HandleAsync(new ApproveOtClientProcedureCommand
        {
            OtTenantId = OtTenant,
            ProcedureInstanceId = procedureId,
            ApprovedBy = Approver,
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ApproveOtClientProcedureStatus.Approved);
        result.Procedure!.Status.Should().Be(TramiteEstado.Aprobado);

        await using var verify = NewContext(db);
        var history = await verify.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        history.ToStatus.Should().Be(TramiteEstado.Aprobado);
        history.ChangedBy.Should().Be(Approver);
        history.Metadata.Should().Contain(OtTenant.ToString());
    }

    [Fact]
    public async Task AC3_Reject_PersistsReasonAndRejectedOt()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        const string reason = "Documentacion incompleta";

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var handler = NewRejectHandler(ctx);
        var result = await handler.HandleAsync(new RejectOtClientProcedureCommand
        {
            OtTenantId = OtTenant,
            ProcedureInstanceId = procedureId,
            RejectedBy = Approver,
            Request = new() { Reason = reason },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(RejectOtClientProcedureStatus.Rejected);
        result.Procedure!.Status.Should().Be(TramiteEstado.Rechazado);

        await using var verify = NewContext(db);
        var history = await verify.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        history.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task AC4_GetById_ReturnsNotFoundForUnlinkedClient()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedOt(seed, OtherOtTenant, OtherOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            // N 03: el OT solo decide sobre 'entregado'; un borrador NO es aprobable.
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Borrador);
        }

        await using var ctx = NewContext(db);
        var handler = new GetOtClientProcedureHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var result = await handler.HandleAsync(new GetOtClientProcedureQuery
        {
            OtTenantId = OtherOtTenant,
            ProcedureInstanceId = procedureId,
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(GetOtClientProcedureStatus.NotFound);
    }

    [Fact]
    public async Task AC5_List_FiltersByProcedureTypeAndPaginates()
    {
        var db = NewDbName();
        var typeA1 = Guid.NewGuid();
        var typeA2 = Guid.NewGuid();
        var typeB1 = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedProcedure(seed, typeA1, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, "REF-A1");
            SeedProcedure(seed, typeA2, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, "REF-A2");
            SeedProcedure(seed, typeB1, ClientTenant, TransitOffice, ProcedureTypeB, TramiteEstado.Entregado, "REF-B1");
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            ProcedureTypeId = ProcedureTypeA,
            Page = 1,
            PageSize = 20,
        }, TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Data.Should().OnlyContain(p => p.ProcedureTypeId == ProcedureTypeA);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task AC1_ExcludesProceduresWhenGrantDisabled()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice, isEnabled: false);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var result = await handler.HandleAsync(new ListOtClientProceduresQuery { OtTenantId = OtTenant }, TestContext.Current.CancellationToken);

        result.Data.Should().BeEmpty();
    }

    [Fact] // HU #10432 AC1 — transición manual: source=ot_admin + changed_by sellado
    public async Task Approve_ManualSource_RecordsOtAdminAndChangedBy()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var updated = await repo.ApproveAsync(
            OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        await using var verify = NewContext(db);
        var history = await verify.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        history.ToStatus.Should().Be(TramiteEstado.Aprobado);
        history.ChangedBy.Should().Be(Approver);
        history.Metadata.Should().Contain("source").And.Contain(OtTransitionSource.OtAdmin);
    }

    [Fact] // HU #10432 AC1 — transición por webhook: source=quipux_webhook, changed_by null (sistema)
    public async Task Reject_WebhookSource_RecordsQuipuxWebhookAndNullChangedBy()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            // N 03: el webhook Quipux decide sobre un trámite 'entregado'.
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var updated = await repo.RejectAsync(
            OtTenant, procedureId, "Rechazado vía integración Quipux", rejectedBy: null,
            OtTransitionSource.QuipuxWebhook, TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        await using var verify = NewContext(db);
        var history = await verify.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        history.ToStatus.Should().Be(TramiteEstado.Rechazado);
        history.ChangedBy.Should().BeNull();
        history.Metadata.Should().Contain("source").And.Contain(OtTransitionSource.QuipuxWebhook);
    }

    [Fact] // HU #10432 AC2 (negativo) — transición inválida (origen != pending_ot) no inserta fila
    public async Task Approve_WhenNotPendingOt_ReturnsNullAndNoHistory()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            // N 03: el OT solo decide sobre 'entregado'; un borrador NO es aprobable.
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Borrador);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var updated = await repo.ApproveAsync(
            OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, TestContext.Current.CancellationToken);

        updated.Should().BeNull();
        await using var verify = NewContext(db);
        var hasHistory = await verify.ProcedureInstanceStatusHistories
            .AnyAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        hasHistory.Should().BeFalse();
    }

    [Fact] // Consolidado/LT OT — el acceso puntual soporta el override de organismo del SuperAdmin.
    public async Task GetById_ConOverrideDeOrganismo_ResuelveSinPerfilOtDelTenant()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var superAdminTenant = Guid.NewGuid(); // sin TransitOfficeProfile

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        // Sin override, un tenant sin perfil OT no ve nada (el bug que ocultaba los entregados).
        var sinOverride = await repo.GetByIdAsync(
            superAdminTenant, procedureId, TestContext.Current.CancellationToken);
        sinOverride.Should().BeNull();

        // Con override del organismo, el SuperAdmin accede al trámite del cliente.
        var conOverride = await repo.GetByIdAsync(
            superAdminTenant, procedureId, TransitOffice, TestContext.Current.CancellationToken);
        conOverride.Should().NotBeNull();
        conOverride!.ClientTenantId.Should().Be(ClientTenant);
        conOverride.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact] // El executor de scope cliente ejecuta la acción (passthrough en InMemory, RLS en Postgres).
    public async Task ExecuteInClientTenantScope_EjecutaLaAccion()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var ran = await repo.ExecuteInClientTenantScopeAsync(
            ClientTenant,
            () => Task.FromResult(42),
            TestContext.Current.CancellationToken);

        ran.Should().Be(42);
    }

    // ---------- HU #10654 (Feature #10587): el OT asigna placa a un trámite en preasignado ----------

    [Fact]
    public async Task AssignPlate_Preasignado_ReservaPlacaYPasaAAsignado()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Preasignado);
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ABC100", Approver, OtTransitionSource.OtAdmin, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Asignado);
        var detail = await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken);
        detail.State.Should().Be("preasignada");
        detail.ProcedureInstanceId.Should().Be(procedureId);
        (await verify.ProcedureInstanceFieldValues.AnyAsync(
            f => f.ProcedureInstanceId == procedureId && f.FieldKey == "plate" && f.ValueText == "ABC100",
            TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task AssignPlate_NoPreasignado_DevuelveNull()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ABC100", Approver, OtTransitionSource.OtAdmin, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    // ---------- HU #10655: aprobar RUNT (placa utilizada) / revocar (placa revocada) ----------

    [Fact]
    public async Task Approve_Asignado_MarcaPlacaUtilizada()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Asignado);
            // Gate SOAT (HU #10611): sin soat_estado=vigente el OT no puede aprobar.
            seed.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), ProcedureInstanceId = procedureId, TenantId = ClientTenant,
                FieldKey = "soat_estado", ValueText = "vigente",
            });
            var plateRepo = new PlateRangeRepository(seed);
            await plateRepo.CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
            await plateRepo.TryReservePlateAsync(ClientTenant, TransitOffice, "ABC100", procedureId, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var updated = await repo.ApproveAsync(OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Aprobado);
        (await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken))
            .State.Should().Be("utilizada");
    }

    [Fact]
    public async Task Revoke_Asignado_LiberaPlacaYVuelveAPreasignado()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Asignado);
            var plateRepo = new PlateRangeRepository(seed);
            await plateRepo.CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
            await plateRepo.TryReservePlateAsync(ClientTenant, TransitOffice, "ABC100", procedureId, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var updated = await repo.RevokePlateAsync(OtTenant, procedureId, "Error en la placa", Approver, OtTransitionSource.OtAdmin, TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Preasignado);
        var detail = await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken);
        detail.State.Should().Be("revocada");
        detail.ProcedureInstanceId.Should().BeNull();
    }

    // ---------- HU #10610 (R06): SOAT gate duro en la aprobación de la ruta de placa ----------

    [Theory]
    [InlineData("vencido", false)]  // SOAT vencido → bloquea la aprobación (gate duro).
    [InlineData("vigente", true)]   // SOAT vigente → aprueba.
    [InlineData("unknown", false)]  // HU #10611: sin evidencia de SOAT vigente → bloquea.
    [InlineData(null, false)]       // HU #10611: ausente → bloquea (el OT exige SOAT registrado).
    public async Task Approve_Asignado_SoatGateDuro(string? soatEstado, bool expectApproved)
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Asignado);
            if (soatEstado is not null)
            {
                seed.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(), ProcedureInstanceId = procedureId, TenantId = ClientTenant,
                    FieldKey = "soat_estado", ValueText = soatEstado,
                });
                seed.SaveChanges();
            }
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var updated = await repo.ApproveAsync(OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, TestContext.Current.CancellationToken);

        await using var verify = NewContext(db);
        var status = (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken)).Status;
        if (expectApproved)
        {
            updated.Should().NotBeNull();
            status.Should().Be(TramiteEstado.Aprobado);
        }
        else
        {
            updated.Should().BeNull();
            status.Should().Be(TramiteEstado.Asignado); // bloqueado, sin cambiar
        }
    }

    private static void SeedOt(FlitDbContext ctx, Guid otTenantId, Guid transitOfficeId)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = otTenantId,
            TransitOfficeId = transitOfficeId,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedGrant(
        FlitDbContext ctx,
        Guid clientTenantId,
        Guid transitOfficeId,
        bool isEnabled = true)
    {
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = clientTenantId,
            TransitOfficeId = transitOfficeId,
            IsEnabled = isEnabled,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedCatalog(
        FlitDbContext ctx,
        Guid clientTenantId,
        Guid procedureTypeId,
        string tenantLegalName,
        string procedureTypeName)
    {
        if (!ctx.Tenants.Any(t => t.Id == clientTenantId))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = clientTenantId,
                Code = "client",
                LegalName = tenantLegalName,
                TaxId = "900000000",
                TenantType = "client",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        if (!ctx.ProcedureTypes.Any(pt => pt.Id == procedureTypeId))
        {
            ctx.ProcedureTypes.Add(new ProcedureType
            {
                Id = procedureTypeId,
                Code = "matricula_inicial",
                Name = procedureTypeName,
                Family = "MATRICULAS",
                IsActive = true,
                PublicationStatus = PublicationStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

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

    private static void SeedProcedure(
        FlitDbContext ctx,
        Guid id,
        Guid clientTenantId,
        Guid transitOfficeId,
        Guid procedureTypeId,
        string status,
        string reference = "REF-001")
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = clientTenantId,
            ProcedureTypeId = procedureTypeId,
            ReferenceNumber = reference,
            Status = status,
            TransitOfficeId = transitOfficeId,
            CreatedByUserId = ActorUser,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static ApproveOtClientProcedureHandler NewApproveHandler(FlitDbContext ctx) =>
        new(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()), new AllowAllQuipuxGuard());

    private static RejectOtClientProcedureHandler NewRejectHandler(FlitDbContext ctx) =>
        new(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()), new AllowAllQuipuxGuard());

    private sealed class AllowAllQuipuxGuard : IQuipuxReadOnlyGuard
    {
        public Task<QuipuxReadOnlyResult> ValidateActionAsync(
            Guid tenantId,
            string action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(QuipuxReadOnlyResult.Allowed());
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
