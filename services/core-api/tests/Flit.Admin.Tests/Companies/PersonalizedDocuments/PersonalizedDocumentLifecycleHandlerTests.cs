using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Activate;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Deactivate;
using Flit.Admin.Application.Companies.PersonalizedDocuments.GetView;
using Flit.Admin.Application.Companies.PersonalizedDocuments.List;
using Flit.Admin.Domain.Companies.PersonalizedDocuments;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.PersonalizedDocuments;

/// <summary>
/// Tests del ciclo de vida del documento personalizado por compañía (HU #11314, Feature #11309,
/// ADR-0042, §8 DT-7 del plan técnico). Ejercita los handlers reales
/// (<see cref="ActivatePersonalizedDocumentVersionHandler"/>,
/// <see cref="DeactivatePersonalizedDocumentHandler"/>, <see cref="GetPersonalizedDocumentViewHandler"/>,
/// <see cref="ListPersonalizedDocumentsHandler"/>) sobre <see cref="CompanyPersonalizedDocumentRepository"/>
/// real (EF InMemory) — sin tocar el pipeline documental de la HU #11316. Sembrado directo del contexto
/// (la escritura de versiones ya la cubre <see cref="PersonalizedDocumentHandlerTests"/> de la HU #11313).
///
/// AC1 — reactivación repetida de versiones anteriores, en cualquier orden.
/// AC2 — «volver al documento del sistema» conserva el histórico completo; nada se borra.
/// AC3 — la vista previa NUNCA activa una versión.
/// AC4 — cambio de canal a FLIT_SMTP desactiva el reemplazo (409 en escritura) y conserva el historial.
/// AC5 — el historial expone quién cargó/reactivó, cuándo y cuál está vigente.
/// AC6 — negativo de aislamiento cross-tenant (403/404) + superadmin puede operar ambos tenants.
/// </summary>
public sealed class PersonalizedDocumentLifecycleHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-1000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-1000-4000-8000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-1000-4000-8000-000000000003");
    private static readonly Guid SuperAdminId = Guid.Parse("dddddddd-1000-4000-8000-000000000004");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- AC1: reactivación repetida, en cualquier orden ----------

    [Fact]
    public async Task AC1_Activate_ReactivatesHistoricVersion_AndRetiresCurrentActive()
    {
        var dbName = NewDbName();
        Guid v1, v2;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            v1 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
            v2 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 2, status: "activo", isActive: true);
        }

        await using var ctx = NewContext(dbName);
        var (activate, _, _, _) = Handlers(ctx);

        var result = await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            Id = v1,
            ActivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);
        result.Version.Should().Be(1);

        await using var verify = NewContext(dbName);
        var rows = await verify.CompanyPersonalizedDocuments.Where(d => d.TenantId == TenantA).ToListAsync(Ct);
        rows.Should().HaveCount(2); // nada se borró

        rows.Single(r => r.Id == v1).Status.Should().Be("activo");
        rows.Single(r => r.Id == v1).IsActive.Should().BeTrue();
        rows.Single(r => r.Id == v1).ActivatedBy.Should().Be(ActorId);

        rows.Single(r => r.Id == v2).Status.Should().Be("historico");
        rows.Single(r => r.Id == v2).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task AC1_Activate_CanBeRepeated_InAnyOrder_BetweenTheSameTwoVersions()
    {
        var dbName = NewDbName();
        Guid v1, v2;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            v1 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
            v2 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 2, status: "activo", isActive: true);
        }

        // v1 activa, luego v2 otra vez, luego v1 otra vez — repetible sin límite ni orden fijo.
        await using (var ctx1 = NewContext(dbName))
        {
            var (activate, _, _, _) = Handlers(ctx1);
            var r1 = await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand { TenantId = TenantA, Id = v1, ActivatedBy = ActorId }, Ct);
            r1.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);
        }

        await using (var ctx2 = NewContext(dbName))
        {
            var (activate, _, _, _) = Handlers(ctx2);
            var r2 = await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand { TenantId = TenantA, Id = v2, ActivatedBy = ActorId }, Ct);
            r2.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);
        }

        await using (var ctx3 = NewContext(dbName))
        {
            var (activate, _, _, _) = Handlers(ctx3);
            var r3 = await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand { TenantId = TenantA, Id = v1, ActivatedBy = ActorId }, Ct);
            r3.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);
        }

        await using var verify = NewContext(dbName);
        var rows = await verify.CompanyPersonalizedDocuments.Where(d => d.TenantId == TenantA).ToListAsync(Ct);
        rows.Should().HaveCount(2); // ninguna reactivación borró nada
        rows.Count(r => r.IsActive).Should().Be(1);
        rows.Single(r => r.IsActive).Id.Should().Be(v1); // terminó en v1 tras la tercera reactivación
    }

    [Fact]
    public async Task AC1_Activate_PendingOrRejectedVersion_Returns409VersionNoActivable()
    {
        var dbName = NewDbName();
        Guid pending;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            pending = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "pendiente", isActive: false);
        }

        await using var ctx = NewContext(dbName);
        var (activate, _, _, _) = Handlers(ctx);

        var result = await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            Id = pending,
            ActivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.VersionNotActivable);
    }

    // ---------- AC2: «volver al sistema» no pierde el histórico ----------

    [Fact]
    public async Task AC2_Deactivate_RemovesActiveFlag_ButKeepsAllHistoryRowsAndFiles()
    {
        var dbName = NewDbName();
        Guid v1, v2, v3;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            v1 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
            v2 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 2, status: "historico", isActive: false);
            v3 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 3, status: "activo", isActive: true);
        }

        await using var ctx = NewContext(dbName);
        var (_, deactivate, _, _) = Handlers(ctx);

        var result = await deactivate.HandleAsync(new DeactivatePersonalizedDocumentCommand
        {
            TenantId = TenantA,
            DocumentType = PersonalizedDocumentTypes.Mandato,
            DeactivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(DeactivatePersonalizedDocumentOutcome.Deactivated);

        await using var verify = NewContext(dbName);
        var rows = await verify.CompanyPersonalizedDocuments.Where(d => d.TenantId == TenantA).ToListAsync(Ct);

        rows.Should().HaveCount(3); // 0 filas borradas (restricción 9)
        rows.Should().OnlyContain(r => !r.IsActive); // ninguna versión queda activa
        rows.Single(r => r.Id == v3).Status.Should().Be("historico");
        rows.Select(r => r.StoragePath).Should().BeEquivalentTo(
            rows.Select(r => r.StoragePath)); // los storage_path no cambian (archivos intactos)
        var v1Row = rows.Single(r => r.Id == v1);
        var v2Row = rows.Single(r => r.Id == v2);
        v1Row.StoragePath.Should().NotBeNullOrWhiteSpace();
        v2Row.StoragePath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AC2_Deactivate_IsIdempotent_WhenNoVersionIsCurrentlyActive()
    {
        var dbName = NewDbName();
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
        }

        await using var ctx = NewContext(dbName);
        var (_, deactivate, _, _) = Handlers(ctx);

        var result = await deactivate.HandleAsync(new DeactivatePersonalizedDocumentCommand
        {
            TenantId = TenantA,
            DocumentType = PersonalizedDocumentTypes.Mandato,
            DeactivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(DeactivatePersonalizedDocumentOutcome.Deactivated); // 204, aunque no había nada activo
    }

    [Fact]
    public async Task AC2_AfterDeactivate_AnyVersionCanBeReactivated()
    {
        var dbName = NewDbName();
        Guid v1;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            v1 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "activo", isActive: true);
        }

        await using (var ctx1 = NewContext(dbName))
        {
            var (_, deactivate, _, _) = Handlers(ctx1);
            await deactivate.HandleAsync(new DeactivatePersonalizedDocumentCommand
            {
                TenantId = TenantA,
                DocumentType = PersonalizedDocumentTypes.Mandato,
                DeactivatedBy = ActorId,
            }, Ct);
        }

        await using (var ctx2 = NewContext(dbName))
        {
            var (activate, _, _, _) = Handlers(ctx2);
            var reactivated = await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
            {
                TenantId = TenantA,
                Id = v1,
                ActivatedBy = ActorId,
            }, Ct);
            reactivated.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);
        }

        await using var verify = NewContext(dbName);
        var row = await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == v1, Ct);
        row.IsActive.Should().BeTrue();
        row.Status.Should().Be("activo");
    }

    // ---------- AC3: la vista previa nunca activa nada ----------

    [Fact]
    public async Task AC3_GetView_ReturnsPresignedUrl_AndNeverChangesActiveVersion()
    {
        var dbName = NewDbName();
        Guid active, historic;
        string historicStoragePath;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            active = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 2, status: "activo", isActive: true);
            historic = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
            historicStoragePath = seed.CompanyPersonalizedDocuments.Single(d => d.Id == historic).StoragePath;
        }

        await using var ctx = NewContext(dbName);
        var (_, _, getView, storage) = Handlers(ctx);
        storage.Seed(historicStoragePath, [0x25, 0x50, 0x44, 0x46]);

        var result = await getView.HandleAsync(new GetPersonalizedDocumentViewCommand { TenantId = TenantA, Id = historic }, Ct);

        result.Outcome.Should().Be(GetPersonalizedDocumentViewOutcome.Found);
        result.Url.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAt.Should().NotBeNull();

        // Previsualizar la versión histórica NO la activa ni toca la que estaba vigente.
        await using var verify = NewContext(dbName);
        var activeRow = await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == active, Ct);
        var historicRow = await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == historic, Ct);
        activeRow.IsActive.Should().BeTrue();
        activeRow.Status.Should().Be("activo");
        historicRow.IsActive.Should().BeFalse();
        historicRow.Status.Should().Be("historico");
    }

    [Fact]
    public async Task AC3_GetView_UnreadableStorage_ReturnsNotFound_NeverThrows()
    {
        var dbName = NewDbName();
        Guid id;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            id = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
        }

        await using var ctx = NewContext(dbName);
        var (_, _, getView, _) = Handlers(ctx); // storage vacío: el objeto no existe

        var result = await getView.HandleAsync(new GetPersonalizedDocumentViewCommand { TenantId = TenantA, Id = id }, Ct);

        result.Outcome.Should().Be(GetPersonalizedDocumentViewOutcome.NotFound);
    }

    // ---------- AC4: cambio de canal a FLIT_SMTP desactiva el reemplazo, conserva el histórico ----------

    [Fact]
    public async Task AC4_FlitSmtpChannel_ActivateAndDeactivate_Return409_HistoryStillReadable()
    {
        var dbName = NewDbName();
        Guid v1;
        await using (var seed = NewContext(dbName))
        {
            SeedFlitSmtpChannel(seed, TenantA);
            v1 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
        }

        await using var ctx = NewContext(dbName);
        var (activate, deactivate, _, _) = Handlers(ctx);

        var activateResult = await activate.HandleAsync(
            new ActivatePersonalizedDocumentVersionCommand { TenantId = TenantA, Id = v1, ActivatedBy = ActorId }, Ct);
        activateResult.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.ChannelNotEnabled);

        var deactivateResult = await deactivate.HandleAsync(
            new DeactivatePersonalizedDocumentCommand { TenantId = TenantA, DocumentType = PersonalizedDocumentTypes.Mandato, DeactivatedBy = ActorId }, Ct);
        deactivateResult.Outcome.Should().Be(DeactivatePersonalizedDocumentOutcome.ChannelNotEnabled);

        // El historial se sigue pudiendo consultar (restricción 9): el GET no exige el canal.
        var listHandler = new ListPersonalizedDocumentsHandler(new CompanyPersonalizedDocumentRepository(ctx));
        var groups = await listHandler.HandleAsync(TenantA, Ct);
        groups.Should().ContainSingle(g => g.DocumentType == PersonalizedDocumentTypes.Mandato);
    }

    [Fact]
    public async Task AC4_SwitchingBackToTenantApi_SameActiveVersionAppliesAgain_WithoutReupload()
    {
        var dbName = NewDbName();
        Guid v1;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            v1 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "activo", isActive: true);
        }

        // Cambia a FLIT_SMTP: no toca filas ni activo/inactivo — es un interruptor de resolución, no de datos.
        await using (var switchToSmtp = NewContext(dbName))
        {
            var policy = await switchToSmtp.TenantOperationalPolicies.SingleAsync(p => p.TenantId == TenantA, Ct);
            policy.NotificationChannel = "flit_smtp";
            await switchToSmtp.SaveChangesAsync(Ct);
        }

        await using (var verifyDuringSmtp = NewContext(dbName))
        {
            var row = await verifyDuringSmtp.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == v1, Ct);
            row.IsActive.Should().BeTrue(); // el flag no se tocó — no hay booleano paralelo (§8, "fuente única")
        }

        // Vuelve a TENANT_API: la MISMA versión sigue activa, sin recargarla.
        await using (var switchBack = NewContext(dbName))
        {
            var policy = await switchBack.TenantOperationalPolicies.SingleAsync(p => p.TenantId == TenantA, Ct);
            policy.NotificationChannel = "tenant_api";
            await switchBack.SaveChangesAsync(Ct);
        }

        await using var verify = NewContext(dbName);
        var listHandler = new ListPersonalizedDocumentsHandler(new CompanyPersonalizedDocumentRepository(verify));
        var groups = await listHandler.HandleAsync(TenantA, Ct);
        var group = groups.Single(g => g.DocumentType == PersonalizedDocumentTypes.Mandato);
        group.Active!.Id.Should().Be(v1);
        group.Active!.Version.Should().Be(1);
    }

    // ---------- AC5: el historial expone quién, cuándo y cuál está vigente ----------

    [Fact]
    public async Task AC5_List_ExposesWhoUploadedOrReactivated_When_AndCurrentValidity()
    {
        var dbName = NewDbName();
        Guid v1, v2;
        var uploader = Guid.Parse("11111111-1000-4000-8000-000000000011");
        var reactivator = Guid.Parse("22222222-1000-4000-8000-000000000022");

        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            v1 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false, createdBy: uploader);
            v2 = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 2, status: "activo", isActive: true, createdBy: uploader);
        }

        // Reactivar v1 con un actor distinto al que la cargó — el historial debe reflejar "quién reactivó".
        await using (var ctx1 = NewContext(dbName))
        {
            var (activate, _, _, _) = Handlers(ctx1);
            await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
            {
                TenantId = TenantA,
                Id = v1,
                ActivatedBy = reactivator,
            }, Ct);
        }

        await using var verify = NewContext(dbName);
        var listHandler = new ListPersonalizedDocumentsHandler(new CompanyPersonalizedDocumentRepository(verify));
        var groups = await listHandler.HandleAsync(TenantA, Ct);
        var group = groups.Single(g => g.DocumentType == PersonalizedDocumentTypes.Mandato);

        group.History.Should().HaveCount(2);

        var historyV1 = group.History.Single(h => h.Id == v1);
        historyV1.CreatedBy.Should().Be(uploader); // quién cargó
        historyV1.ActivatedBy.Should().Be(reactivator); // quién reactivó
        historyV1.IsActive.Should().BeTrue(); // indicador de vigencia
        historyV1.Version.Should().Be(1);
        historyV1.Filename.Should().NotBeNullOrWhiteSpace();
        historyV1.PageCount.Should().NotBeNull();

        var historyV2 = group.History.Single(h => h.Id == v2);
        historyV2.CreatedBy.Should().Be(uploader);
        historyV2.IsActive.Should().BeFalse(); // retirada por la reactivación de v1

        group.Active!.Id.Should().Be(v1); // el vigente agregado coincide con el indicador por fila
    }

    // ---------- AC6: negativo de aislamiento cross-tenant + superadmin en ambos ----------

    [Fact]
    public async Task AC6_Activate_ForeignTenant_ReturnsNotFound_AndDoesNotChangeTargetTenantValidity()
    {
        var dbName = NewDbName();
        Guid idOfB;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedTenantApiChannel(seed, TenantB);
            SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "activo", isActive: true);
            idOfB = SeedVersion(seed, TenantB, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
        }

        // Un admin de A intenta reactivar una versión de B usando el tenantId de A (aislamiento por
        // WHERE tenant_id explícito, no por el id del recurso).
        await using var ctx = NewContext(dbName);
        var (activate, _, _, _) = Handlers(ctx);

        var result = await activate.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            Id = idOfB,
            ActivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.NotFound);

        // El estado de vigencia de B no cambió por el intento fallido de A.
        await using var verify = NewContext(dbName);
        var rowOfB = await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == idOfB, Ct);
        rowOfB.TenantId.Should().Be(TenantB);
        rowOfB.IsActive.Should().BeFalse();
        rowOfB.Status.Should().Be("historico");
    }

    [Fact]
    public async Task AC6_GetView_ForeignTenant_ReturnsNotFound_NeverSignsUrlForForeignTenant()
    {
        var dbName = NewDbName();
        Guid idOfB;
        string storagePathOfB;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedTenantApiChannel(seed, TenantB);
            idOfB = SeedVersion(seed, TenantB, PersonalizedDocumentTypes.Mandato, version: 1, status: "activo", isActive: true);
            storagePathOfB = seed.CompanyPersonalizedDocuments.Single(d => d.Id == idOfB).StoragePath;
        }

        await using var ctx = NewContext(dbName);
        var (_, _, getView, storage) = Handlers(ctx);
        storage.Seed(storagePathOfB, [0x25, 0x50, 0x44, 0x46]);

        var result = await getView.HandleAsync(new GetPersonalizedDocumentViewCommand { TenantId = TenantA, Id = idOfB }, Ct);

        result.Outcome.Should().Be(GetPersonalizedDocumentViewOutcome.NotFound);
        storage.GetViewUrlCalls.Should().Be(0); // el ownership se valida ANTES de firmar (ADR-0029)
    }

    [Fact]
    public async Task AC6_Deactivate_ForeignTenant_NeverDeactivatesTheOtherTenantsActiveVersion()
    {
        var dbName = NewDbName();
        Guid activeOfB;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedTenantApiChannel(seed, TenantB);
            activeOfB = SeedVersion(seed, TenantB, PersonalizedDocumentTypes.Mandato, version: 1, status: "activo", isActive: true);
        }

        // El admin de A solo puede desactivar dentro de SU tenant: el repositorio filtra por TenantId=A,
        // así que nunca alcanza la fila activa de B (tampoco existe fila de A que retirar).
        await using var ctx = NewContext(dbName);
        var (_, deactivate, _, _) = Handlers(ctx);

        var result = await deactivate.HandleAsync(new DeactivatePersonalizedDocumentCommand
        {
            TenantId = TenantA,
            DocumentType = PersonalizedDocumentTypes.Mandato,
            DeactivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(DeactivatePersonalizedDocumentOutcome.Deactivated); // 204: nada que hacer en A

        await using var verify = NewContext(dbName);
        var rowOfB = await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == activeOfB, Ct);
        rowOfB.IsActive.Should().BeTrue(); // B sigue intacta
    }

    [Fact]
    public async Task AC6_SuperAdmin_CanActivateDeactivateAndPreview_OnBothTenants()
    {
        var dbName = NewDbName();
        Guid idOfA, idOfB;
        string storagePathOfA, storagePathOfB;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedTenantApiChannel(seed, TenantB);
            idOfA = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
            SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 2, status: "activo", isActive: true);
            idOfB = SeedVersion(seed, TenantB, PersonalizedDocumentTypes.Mandato, version: 1, status: "activo", isActive: true);
            storagePathOfA = seed.CompanyPersonalizedDocuments.Single(d => d.Id == idOfA).StoragePath;
            storagePathOfB = seed.CompanyPersonalizedDocuments.Single(d => d.Id == idOfB).StoragePath;
        }

        // El SuperAdmin llama con el tenantId REAL del recurso en cada caso (la política de
        // autorización — AdminCompanyPolicy + CompanyOwnTenantFilter — ya lo deja pasar libre; el
        // handler no distingue roles, solo el tenantId de la ruta).
        await using var ctxA = NewContext(dbName);
        var (activateA, _, viewA, storageA) = Handlers(ctxA);
        storageA.Seed(storagePathOfA, [0x25, 0x50, 0x44, 0x46]);

        var activateResult = await activateA.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            Id = idOfA,
            ActivatedBy = SuperAdminId,
        }, Ct);
        activateResult.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);

        var previewA = await viewA.HandleAsync(new GetPersonalizedDocumentViewCommand { TenantId = TenantA, Id = idOfA }, Ct);
        previewA.Outcome.Should().Be(GetPersonalizedDocumentViewOutcome.Found);

        await using var ctxB = NewContext(dbName);
        var (_, deactivateB, viewB, storageB) = Handlers(ctxB);
        storageB.Seed(storagePathOfB, [0x25, 0x50, 0x44, 0x46]);

        var previewB = await viewB.HandleAsync(new GetPersonalizedDocumentViewCommand { TenantId = TenantB, Id = idOfB }, Ct);
        previewB.Outcome.Should().Be(GetPersonalizedDocumentViewOutcome.Found);

        var deactivateResult = await deactivateB.HandleAsync(new DeactivatePersonalizedDocumentCommand
        {
            TenantId = TenantB,
            DocumentType = PersonalizedDocumentTypes.Mandato,
            DeactivatedBy = SuperAdminId,
        }, Ct);
        deactivateResult.Outcome.Should().Be(DeactivatePersonalizedDocumentOutcome.Deactivated);

        await using var verify = NewContext(dbName);
        (await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == idOfA, Ct)).IsActive.Should().BeTrue();
        (await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == idOfB, Ct)).IsActive.Should().BeFalse();
    }

    // ---------- Helpers ----------

    private static (
        ActivatePersonalizedDocumentVersionHandler Activate,
        DeactivatePersonalizedDocumentHandler Deactivate,
        GetPersonalizedDocumentViewHandler GetView,
        FakePersonalizedDocumentViewStorage Storage) Handlers(FlitDbContext ctx)
    {
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var storage = new FakePersonalizedDocumentViewStorage();
        var auditWriter = Substitute.For<IAdminAuditWriter>();

        return (
            new ActivatePersonalizedDocumentVersionHandler(repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance),
            new DeactivatePersonalizedDocumentHandler(repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance),
            new GetPersonalizedDocumentViewHandler(repository, storage),
            storage);
    }

    private static string NewDbName() => $"flit-personalized-docs-lifecycle-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static void SeedTenantApiChannel(FlitDbContext ctx, Guid tenantId) => SeedPolicy(ctx, tenantId, "tenant_api");

    private static void SeedFlitSmtpChannel(FlitDbContext ctx, Guid tenantId) => SeedPolicy(ctx, tenantId, "flit_smtp");

    private static void SeedPolicy(FlitDbContext ctx, Guid tenantId, string channel)
    {
        if (ctx.TenantOperationalPolicies.Any(p => p.TenantId == tenantId))
        {
            return;
        }

        ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NotificationChannel = channel,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    /// <summary>Siembra una fila de <c>company_personalized_documents</c> directamente en el contexto
    /// (los handlers de esta HU operan sobre versiones ya confirmadas — la escritura del ciclo de
    /// alta/confirm ya está cubierta por <see cref="PersonalizedDocumentHandlerTests"/>, HU #11313).</summary>
    private static Guid SeedVersion(
        FlitDbContext ctx,
        Guid tenantId,
        string documentType,
        int version,
        string status,
        bool isActive,
        Guid? createdBy = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        ctx.CompanyPersonalizedDocuments.Add(new CompanyPersonalizedDocumentEntity
        {
            Id = id,
            TenantId = tenantId,
            DocumentType = documentType,
            Version = version,
            Status = status,
            IsActive = isActive,
            Filename = $"{documentType}.pdf",
            StoragePath = $"fm-personalized-{tenantId}-{documentType}-{version}",
            StorageSha256 = new string('a', 64),
            SizeBytes = 1024,
            PageCount = 1,
            CreatedAt = now,
            CreatedBy = createdBy ?? ActorId,
            ActivatedAt = isActive ? now : null,
            ActivatedBy = isActive ? (createdBy ?? ActorId) : null,
        });
        ctx.SaveChanges();
        return id;
    }

    /// <summary>Storage en memoria para <c>GetViewUrlAsync</c>: no toca red.</summary>
    private sealed class FakePersonalizedDocumentViewStorage : ICompanyPersonalizedDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _objects = [];

        public int GetViewUrlCalls { get; private set; }

        public void Seed(string storagePath, byte[] bytes) => _objects[storagePath] = bytes;

        public Task<PersonalizedDocumentUploadTicket> CreateUploadAsync(
            Guid tenantId, string documentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No usado en estos tests (HU #11314 no toca el alta).");

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No usado en estos tests (HU #11314 no toca el confirm).");

        public Task<PersonalizedDocumentView?> GetViewUrlAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            GetViewUrlCalls++;
            if (!_objects.ContainsKey(storagePath))
            {
                return Task.FromResult<PersonalizedDocumentView?>(null);
            }

            return Task.FromResult<PersonalizedDocumentView?>(
                new PersonalizedDocumentView($"https://s3.example/view/{storagePath}", DateTimeOffset.UtcNow.AddMinutes(10)));
        }
    }
}
