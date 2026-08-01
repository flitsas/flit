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
using Flit.Tramites.Domain.Tramites.ValueObjects;

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
    public async Task List_ProyectaVinPlacaActoresYGestor_YFiltraPorPlaca()
    {
        var db = NewDbName();
        var matchId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedCatalog(seed, ClientTenant, ProcedureTypeA, "Flota Andina S.A.S.", "Matrícula inicial");
            SeedActorUser(seed, ActorUser);
            // DisplayName del gestor = "Actor Test" (SeedActorUser).
            SeedProcedure(seed, matchId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado,
                reference: "REF-MATCH", plate: "ABC123");
            SeedProcedure(seed, otherId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado,
                reference: "REF-OTHER", plate: "XYZ999");

            var match = seed.ProcedureInstances.Single(p => p.Id == matchId);
            match.Vin = "9BWZZZ377VT004251";
            match.CompradorNombre = "Luis Comprador";
            match.VendedorNombre = "Ana Vendedora";
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));

        var filtered = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Placa = "ABC",
            SortBy = "placa",
            SortDir = "asc",
        }, TestContext.Current.CancellationToken);

        filtered.Data.Should().ContainSingle();
        filtered.Data[0].Id.Should().Be(matchId);
        filtered.Data[0].Placa.Should().Be("ABC123");
        filtered.Data[0].Vin.Should().Be("9BWZZZ377VT004251");
        filtered.Data[0].CompradorNombre.Should().Be("Luis Comprador");
        filtered.Data[0].VendedorNombre.Should().Be("Ana Vendedora");
        filtered.Data[0].GestorNombre.Should().Be("Actor Test");
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

    // ---------- HU #10871 (AC1): observación subsanable del OT (entregado→subsanacion) ----------

    [Fact] // AC1 — con ítems del checklist, la decisión OT observa (subsanacion) en vez de rechazar.
    public async Task Ac1_Reject_ConItems_TransicionaASubsanacionConChecklistHibridoEnMetadata()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        const string reason = "Faltan documentos y la placa no coincide";

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
            Request = new()
            {
                Reason = reason,
                Items =
                [
                    new OtProcedureObservationItem { Campo = "factura", Detalle = "Falta la factura de compra" },
                    new OtProcedureObservationItem { Campo = "plate", Detalle = "La placa no coincide con el RUNT" },
                ],
            },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(RejectOtClientProcedureStatus.Rejected);
        result.Procedure!.Status.Should().Be(TramiteEstado.Rechazado);

        await using var verify = NewContext(db);
        var history = await verify.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        history.FromStatus.Should().Be(TramiteEstado.Entregado);
        history.ToStatus.Should().Be(TramiteEstado.Rechazado);
        history.Reason.Should().Be(reason);
        history.Metadata.Should().Contain("factura").And.Contain("plate").And.Contain(OtTransitionSource.OtAdmin);
    }

    [Fact] // AC1 (regresión) — sin ítems, la decisión OT sigue siendo rechazo definitivo (comportamiento previo).
    public async Task Ac1_Reject_SinItems_SigueTransicionandoARechazado()
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
            Request = new() { Reason = reason, Items = [] },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(RejectOtClientProcedureStatus.Rejected);
        result.Procedure!.Status.Should().Be(TramiteEstado.Rechazado);
    }

    [Fact] // AC1 — el repositorio persiste el checklist HÍBRIDO (motivo + items) en metadata al observar.
    public async Task Ac1_ObserveAsync_PersisteChecklistHibridoEnMetadata()
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
        var updated = await repo.ObserveAsync(
            OtTenant, procedureId, "Revisar el checklist",
            [new OtProcedureObservationItem { Campo = "aduana", Detalle = "Documento vencido" }],
            Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(TramiteEstado.Rechazado);

        await using var verify = NewContext(db);
        var history = await verify.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        history.ToStatus.Should().Be(TramiteEstado.Rechazado);
        history.ChangedBy.Should().Be(Approver);
        history.Metadata.Should().Contain("aduana").And.Contain("Documento vencido").And.Contain("Revisar el checklist");
    }

    [Fact] // HU #10872 (AC1) — ObserveAsync captura el snapshot de field_values AL ENTRAR a subsanación.
    public async Task Ac1_ObserveAsync_CapturaSnapshotDeFieldValuesEnMetadata()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado);
            seed.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = procedureId,
                TenantId = ClientTenant,
                FieldKey = "vin",
                ValueText = "1HGCM82633A004352",
            });
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var updated = await repo.ObserveAsync(
            OtTenant, procedureId, "Corregir el VIN",
            [new OtProcedureObservationItem { Campo = "vin", Detalle = "No coincide con el RUNT" }],
            Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();

        await using var verify = NewContext(db);
        var history = await verify.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ProcedureInstanceId == procedureId, cancellationToken: TestContext.Current.CancellationToken);
        var observation = SubsanacionObservation.FromJson(history.Metadata);
        observation.Should().NotBeNull();
        observation!.FieldSnapshot.Should().NotBeNull();
        observation.FieldSnapshot!["vin"].Should().Be("1HGCM82633A004352");
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
            OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

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
            OtTransitionSource.QuipuxWebhook, cancellationToken: TestContext.Current.CancellationToken);

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
            OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact] // HU #10785 — el sub-estado avanza preasignado→asignado; el status global permanece 'entregado'.
    public async Task AssignPlate_Preasignado_ReservaPlacaYAvanzaSubEstado()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Preasignado);
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Failure.Should().Be(PlateAssignmentFailure.None);
        await using var verify = NewContext(db);
        var instance = await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken);
        instance.Status.Should().Be(TramiteEstado.Entregado);
        instance.PlateFlowStatus.Should().Be(PlateFlowStatus.Asignado);
        var detail = await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken);
        detail.State.Should().Be("preasignada");
        detail.ProcedureInstanceId.Should().Be(procedureId);
        (await verify.ProcedureInstanceFieldValues.AnyAsync(
            f => f.ProcedureInstanceId == procedureId && f.FieldKey == "plate" && f.ValueText == "ABC100",
            TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact] // La asignación exige el sub-estado 'preasignado'; un entregado estándar (sub-estado null) la rechaza.
    public async Task AssignPlate_NoPreasignado_InformaElSubEstado()
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
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.NotPreassigned);
    }

    // El motivo por el que no se pudo asignar tiene que llegar nombrado hasta el endpoint: el OT
    // reportó que el sistema no le decía que la placa ya estaba tomada, solo no lo dejaba avanzar.

    [Fact]
    public async Task AssignPlate_PlacaYaAsignadaAOtroTramite_LoDistingueDeNoDisponible()
    {
        var db = NewDbName();
        var primero = Guid.NewGuid();
        var segundo = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, primero, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Preasignado);
            SeedProcedure(seed, segundo, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Preasignado);
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));

        var primera = await repo.AssignPlateAsync(OtTenant, primero, "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);
        primera.Succeeded.Should().BeTrue();

        // Segundo trámite, misma placa: ya está tomada.
        var segunda = await repo.AssignPlateAsync(OtTenant, segundo, "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        segunda.Succeeded.Should().BeFalse();
        segunda.Failure.Should().Be(PlateAssignmentFailure.PlateAlreadyAssigned);
        segunda.Procedure.Should().BeNull();
    }

    // Una placa viva en otro trámite no se puede reasignar, aunque ese trámite sea de otra compañía u
    // otro OT y aunque la placa no esté en el inventario de rangos (caso reportado en DEV con QXU030).
    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Aprobado)]
    public async Task AssignPlate_PlacaEnTramiteVivoDeOtraCompania_LoBloquea(string estadoDelOtro)
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var otroTramite = Guid.NewGuid();
        var otraCompania = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Preasignado);
            SeedProcedure(seed, otroTramite, otraCompania, TransitOffice, ProcedureTypeA, estadoDelOtro, reference: "TRM-2026-000018", plate: "ABC100");
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.PlateInUseByAnotherProcedure);
        // El operador tiene que poder ir a mirar el trámite que la retiene.
        result.Detail.Should().Contain("TRM-2026-000018");

        await using var verify = NewContext(db);
        var instance = await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken);
        instance.PlateFlowStatus.Should().Be(PlateFlowStatus.Preasignado);
    }

    [Theory] // Rechazado y anulado liberan la placa: el vehículo puede volver a tramitarse con ella.
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Anulado)]
    public async Task AssignPlate_PlacaEnTramiteCerrado_PermiteAsignar(string estadoDelOtro)
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();
        var otroTramite = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Preasignado);
            SeedProcedure(seed, otroTramite, ClientTenant, TransitOffice, ProcedureTypeA, estadoDelOtro, reference: "TRM-2026-000019", plate: "ABC100");
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
    }

    [Fact] // La placa que ya está en el propio trámite no se bloquea a sí misma (reintento idempotente).
    public async Task AssignPlate_PlacaDelMismoTramite_NoSeBloqueaASiMisma()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Preasignado, plate: "ABC100");
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AssignPlate_PlacaFueraDeLosRangos_InformaNoDisponible()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Preasignado);
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));

        // ZZZ999 no pertenece a ningún rango del OT y no se pidió fuera de rango.
        var result = await repo.AssignPlateAsync(OtTenant, procedureId, "ZZZ999", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.PlateNotAvailable);
    }

    [Fact]
    public async Task AssignPlate_SinPlaca_LoInformaComoDatoFaltante()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));

        var result = await repo.AssignPlateAsync(OtTenant, Guid.NewGuid(), "   ", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Failure.Should().Be(PlateAssignmentFailure.MissingPlate);
    }

    [Fact]
    public async Task AssignPlate_TramiteSinGrantVigente_LoInformaComoNoAccesible()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            await new PlateRangeRepository(seed).CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));

        // El trámite no existe para este OT.
        var result = await repo.AssignPlateAsync(OtTenant, Guid.NewGuid(), "ABC100", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PlateAssignmentFailure.ProcedureNotAccessible);
    }

    // ---------- HU #10655: aprobar RUNT (placa utilizada) / revocar (placa revocada) ----------

    [Fact]
    public async Task Approve_Terminado_MarcaPlacaUtilizada()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Terminado);
            var plateRepo = new PlateRangeRepository(seed);
            await plateRepo.CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
            await plateRepo.TryReservePlateAsync(ClientTenant, TransitOffice, "ABC100", procedureId, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var updated = await repo.ApproveAsync(OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        await using var verify = NewContext(db);
        var instance = await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken);
        instance.Status.Should().Be(TramiteEstado.Aprobado);
        instance.PlateFlowStatus.Should().BeNull(); // el sub-flujo de placa se cierra en el terminal
        (await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken))
            .State.Should().Be("utilizada");
    }

    [Fact] // HU #10785 — revocar libera la placa y devuelve el sub-estado a 'preasignado'; el status
           // global permanece 'entregado'.
    public async Task Revoke_Asignado_LiberaPlacaYVuelveSubEstadoAPreasignado()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Asignado);
            var plateRepo = new PlateRangeRepository(seed);
            await plateRepo.CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
            await plateRepo.TryReservePlateAsync(ClientTenant, TransitOffice, "ABC100", procedureId, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), new PlateRangeRepository(ctx));
        var updated = await repo.RevokePlateAsync(OtTenant, procedureId, "Error en la placa", Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

        updated.Should().NotBeNull();
        await using var verify = NewContext(db);
        var instance = await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken);
        instance.Status.Should().Be(TramiteEstado.Entregado);
        instance.PlateFlowStatus.Should().Be(PlateFlowStatus.Preasignado);
        var detail = await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken);
        detail.State.Should().Be("revocada");
        detail.ProcedureInstanceId.Should().BeNull();
    }

    // ---------- HU #10804 (Feature #10587): la bandeja del OT proyecta soat_estado por trámite ----------

    /// <summary>
    /// Uso de ejemplo: la bandeja del OT expone <c>SoatEstado</c> por fila para que el frontend oculte
    /// Aprobar/Rechazar hasta que la placa esté <c>asignado</c> con SOAT <c>vigente</c>.
    /// </summary>
    [Fact] // HU #10804 — happy path + contrato: soat_estado se proyecta (vigente) y es null sin field.
    public async Task List_ProyectaSoatEstadoPorTramite()
    {
        var db = NewDbName();
        var asignadoConSoat = Guid.NewGuid();
        var preasignadoSinSoat = Guid.NewGuid();
        var estandar = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedProcedure(seed, asignadoConSoat, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, "REF-ASG", PlateFlowStatus.Asignado);
            seed.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = asignadoConSoat,
                TenantId = ClientTenant,
                FieldKey = "soat_estado",
                ValueText = "vigente",
            });
            SeedProcedure(seed, preasignadoSinSoat, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, "REF-PRE", PlateFlowStatus.Preasignado);
            SeedProcedure(seed, estandar, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, "REF-STD");
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Status = TramiteEstado.Entregado,
        }, TestContext.Current.CancellationToken);

        // AC1 — asignado con SOAT vigente: el frontend mostrará Aprobar/Rechazar.
        result.Data.Single(p => p.Id == asignadoConSoat).SoatEstado.Should().Be("vigente");
        // AC2/AC3 — sin SOAT registrado: soat_estado null → el frontend oculta las acciones.
        result.Data.Single(p => p.Id == preasignadoSinSoat).SoatEstado.Should().BeNull();
        // AC4 — ruta estándar: soat_estado null (no aplica) y no gatea la decisión.
        result.Data.Single(p => p.Id == estandar).SoatEstado.Should().BeNull();
    }

    // ---------- HU #10805 (Feature #10587): la bandeja del OT proyecta el dígito de preferencia ----------

    /// <summary>
    /// Uso de ejemplo: la bandeja del OT expone <c>PlatePreferredLastDigit</c> como guía para asignar
    /// placa; es informativo (no obliga) y null cuando el gestor no indicó preferencia.
    /// </summary>
    [Fact] // HU #10805 — happy path + contrato: plate_preferred_last_digit se proyecta y es null sin field.
    public async Task List_ProyectaDigitoDePreferencia()
    {
        var db = NewDbName();
        var conDigito = Guid.NewGuid();
        var sinDigito = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedProcedure(seed, conDigito, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, "REF-DIG", PlateFlowStatus.Preasignado);
            seed.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                ProcedureInstanceId = conDigito,
                TenantId = ClientTenant,
                FieldKey = "plate_preferred_last_digit",
                ValueText = "5",
            });
            SeedProcedure(seed, sinDigito, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, "REF-NODIG", PlateFlowStatus.Preasignado);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Status = TramiteEstado.Entregado,
        }, TestContext.Current.CancellationToken);

        // AC2 — el OT recibe el dígito como guía.
        result.Data.Single(p => p.Id == conDigito).PlatePreferredLastDigit.Should().Be("5");
        // AC4 — sin preferencia: null (no gatea ni obliga nada).
        result.Data.Single(p => p.Id == sinDigito).PlatePreferredLastDigit.Should().BeNull();
    }

    // ---------- Gate duro: OT solo aprueba en null (estándar) o terminado ----------

    [Theory]
    [InlineData("asignado", false)]
    [InlineData("preasignado", false)]
    [InlineData("terminado", true)]
    public async Task Approve_RutaPlaca_SoloTerminado(string plateFlowStatus, bool expectApproved)
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: plateFlowStatus);
        }

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var updated = await repo.ApproveAsync(OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, cancellationToken: TestContext.Current.CancellationToken);

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
            status.Should().Be(TramiteEstado.Entregado);
        }
    }

    [Fact] // OT aprueba vía handler un trámite Terminado (ruta de placa).
    public async Task Approve_RutaPlaca_ViaHandler_DesdeEntregadoSinHitoSintetico()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Terminado);
            var plateRepo = new PlateRangeRepository(seed);
            await plateRepo.CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
            await plateRepo.TryReservePlateAsync(ClientTenant, TransitOffice, "ABC100", procedureId, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await NewApproveHandler(ctx).HandleAsync(new ApproveOtClientProcedureCommand
        {
            OtTenantId = OtTenant,
            ProcedureInstanceId = procedureId,
            ApprovedBy = Approver,
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ApproveOtClientProcedureStatus.Approved);

        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Aprobado);
        (await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken))
            .State.Should().Be("utilizada");
        var history = await verify.ProcedureInstanceStatusHistories
            .Where(h => h.ProcedureInstanceId == procedureId)
            .ToListAsync(TestContext.Current.CancellationToken);
        history.Should().Contain(h => h.FromStatus == TramiteEstado.Entregado && h.ToStatus == TramiteEstado.Aprobado);
        history.Should().NotContain(h => h.ToStatus == TramiteEstado.Entregado); // sin hito sintético asignado→entregado
    }

    [Fact] // OT rechaza vía handler un trámite Terminado; libera placa.
    public async Task Reject_RutaPlaca_ViaHandler_DesdeEntregadoYLiberaPlaca()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            SeedGrant(seed, ClientTenant, TransitOffice);
            SeedActorUser(seed, Approver);
            SeedProcedure(seed, procedureId, ClientTenant, TransitOffice, ProcedureTypeA, TramiteEstado.Entregado, plateFlowStatus: PlateFlowStatus.Terminado);
            var plateRepo = new PlateRangeRepository(seed);
            await plateRepo.CreateRangeAsync(ClientTenant, TransitOffice, "ABC", 100, 105, null, TestContext.Current.CancellationToken);
            await plateRepo.TryReservePlateAsync(ClientTenant, TransitOffice, "ABC100", procedureId, TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await NewRejectHandler(ctx).HandleAsync(new RejectOtClientProcedureCommand
        {
            OtTenantId = OtTenant,
            ProcedureInstanceId = procedureId,
            RejectedBy = Approver,
            Request = new() { Reason = "Placa incorrecta" },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(RejectOtClientProcedureStatus.Rejected);

        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.SingleAsync(p => p.Id == procedureId, TestContext.Current.CancellationToken))
            .Status.Should().Be(TramiteEstado.Rechazado);
        var plate = await verify.PlateRangeDetails.SingleAsync(d => d.Plate == "ABC100", TestContext.Current.CancellationToken);
        plate.State.Should().Be("disponible");
        plate.ProcedureInstanceId.Should().BeNull();
        var history = await verify.ProcedureInstanceStatusHistories
            .Where(h => h.ProcedureInstanceId == procedureId)
            .ToListAsync(TestContext.Current.CancellationToken);
        history.Should().Contain(h => h.FromStatus == TramiteEstado.Entregado && h.ToStatus == TramiteEstado.Rechazado);
        history.Should().NotContain(h => h.ToStatus == TramiteEstado.Entregado); // sin hito sintético
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
        string reference = "REF-001",
        string? plateFlowStatus = null,
        string? plate = null)
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = clientTenantId,
            ProcedureTypeId = procedureTypeId,
            ReferenceNumber = reference,
            Status = status,
            PlateFlowStatus = plateFlowStatus,
            Plate = plate,
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
