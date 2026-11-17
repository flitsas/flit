using Flit.Admin.Application.OtMetrics;
using Flit.Admin.Domain.OtMetrics;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtMetrics;

/// <summary>
/// Lo que el Inicio del organismo (rediseño en 3 dashboards, 2026-11-17) le pide de nuevo a los
/// reportes OT: el tiempo promedio de decisión por periodo de la serie, el total de prioritarios
/// pendientes y la tabla de trámites por cliente corporativo. Todo en DÍAS y sin meta: no hay SLA en
/// el dominio.
/// </summary>
public sealed class OtInicioMetricsTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtraEmpresa = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SinGrant = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OtroOrganismo = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid TipoMatricula = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TipoTraspaso = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TipoOtro = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Reviewer = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── Serie: tiempo promedio de decisión en días ───────────────────────────────────────────

    [Fact] // El punto promedia el turno del organismo de las decisiones que cayeron en ESE periodo,
           // en días. 1 día y 3 días el mismo día → 2 días.
    public async Task Serie_TiempoPromedioDecisionDias_PromediaLasDecisionesDelPeriodo()
    {
        var db = NewDbName();
        var decision = AyerAlMediodia();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", TipoMatricula, decision.AddHours(-24), decision);
            Radicar(seed, "REF-2", TipoMatricula, decision.AddHours(-72), decision,
                decisionStatus: TramiteEstado.Rechazado);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunReportAsync(db, UltimosDias(7));

        var punto = report!.Resumen.Serie.Should()
            .ContainSingle(p => p.TiempoPromedioDecisionDias != null).Subject;
        punto.TiempoPromedioDecisionDias.Should().BeApproximately(2, 0.01);
        punto.Aprobados.Should().Be(1);
        punto.Rechazados.Should().Be(1);
    }

    [Fact] // Sin decisiones el punto va en null, no en 0: un cero se leería como «se decidió al instante».
    public async Task Serie_TiempoPromedioDecisionDias_SinDecisionesEsNull()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", TipoMatricula, DateTimeOffset.UtcNow.AddDays(-2));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunReportAsync(db, UltimosDias(7));

        report!.Resumen.Serie.Should().NotBeEmpty()
            .And.OnlyContain(p => p.TiempoPromedioDecisionDias == null);
    }

    [Fact] // Cada punto promedia SOLO las decisiones de su periodo, y una decisión fuera del rango no
           // entra en ningún punto aunque el trámite se haya recibido dentro.
    public async Task Serie_TiempoPromedioDecisionDias_CadaPuntoSoloSuPeriodo_YFueraDeRangoNoCuenta()
    {
        var db = NewDbName();
        var ayer = AyerAlMediodia();
        var anteayer = ayer.AddDays(-1);

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "AYER", TipoMatricula, ayer.AddHours(-24), ayer);
            // Radicado y decidido el mismo día (06:00 → 18:00 de Bogotá): medio día de turno.
            Radicar(seed, "ANTEAYER", TipoMatricula, anteayer.AddHours(-6), anteayer.AddHours(6));
            // Recibido dentro del rango de 3 días, decidido hoy+1 (fuera del rango pedido, que acaba ayer).
            Radicar(seed, "FUERA", TipoMatricula, anteayer, ayer.AddDays(2));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var hasta = Hoy().AddDays(-1);
        var report = await RunReportAsync(db, new OtMetricsFilter(hasta.AddDays(-2), hasta));

        var conTiempo = report!.Resumen.Serie.Where(p => p.TiempoPromedioDecisionDias != null).ToList();
        conTiempo.Should().HaveCount(2);
        conTiempo.Should().ContainSingle(p => p.Desde == ayer.ToString("yyyy-MM-dd"))
            .Which.TiempoPromedioDecisionDias.Should().BeApproximately(1, 0.01);
        conTiempo.Should().ContainSingle(p => p.Desde == anteayer.ToString("yyyy-MM-dd"))
            .Which.TiempoPromedioDecisionDias.Should().BeApproximately(0.5, 0.01);
    }

    [Fact] // SuperAdmin ve la tabla de un organismo concreto con el override, aunque su tenant no
           // tenga perfil OT; sin override, el mismo tenant no resuelve organismo.
    public async Task ClientesPorFamilia_RespetaElOverrideDeSuperAdmin()
    {
        var db = NewDbName();
        var superAdminTenant = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "A-1", TipoTraspaso, DateTimeOffset.UtcNow.AddDays(-2));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new GetOtClientsByFamilyHandler(new OtMetricsReadRepository(ctx));

        var conOverride = await handler.HandleAsync(
            superAdminTenant, UltimosDias(7), TransitOffice, TestContext.Current.CancellationToken);
        conOverride!.Clientes.Should().ContainSingle().Which.Traspasos.Should().Be(1);

        var sinOverride = await handler.HandleAsync(
            superAdminTenant, UltimosDias(7), null, TestContext.Current.CancellationToken);
        sinOverride.Should().BeNull();
    }

    // ── Prioritarios pendientes ──────────────────────────────────────────────────────────────

    [Fact] // «Trámites prioritarios» cuenta TODOS los prioritarios pendientes; los estancados (> 3 días)
           // siguen siendo un subconjunto aparte. El detalle abre exactamente esos trámites.
    public async Task Panel_CuentaTodosLosPrioritariosPendientes_YElDetalleLosAbre()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "PRIO-NUEVO", TipoMatricula, DateTimeOffset.UtcNow.AddHours(-5), prioritario: true);
            Radicar(seed, "PRIO-VIEJO", TipoMatricula, DateTimeOffset.UtcNow.AddDays(-5), prioritario: true);
            Radicar(seed, "NORMAL", TipoMatricula, DateTimeOffset.UtcNow.AddDays(-5));
            // Decidido: ya no está pendiente aunque sea prioritario.
            Radicar(seed, "PRIO-DECIDIDO", TipoMatricula, DateTimeOffset.UtcNow.AddDays(-3),
                DateTimeOffset.UtcNow.AddDays(-1), prioritario: true);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtMetricsReadRepository(ctx);

        var panel = await new GetOtOperationalPanelHandler(repo).HandleAsync(
            OtTenant, UltimosDias(30), null, TestContext.Current.CancellationToken);

        panel!.Cola.Prioritarios.Should().Be(2);
        panel.Antiguedad.PrioritariosEstancados.Should().Be(1);

        OtDrilldownBuckets.IsKnown(OtDrilldownBuckets.Prioritarios).Should().BeTrue();
        var detalle = await new GetOtDrilldownHandler(repo).HandleAsync(
            OtTenant, UltimosDias(30), OtDrilldownBuckets.Prioritarios, null,
            TestContext.Current.CancellationToken);

        detalle!.Total.Should().Be(2);
        detalle.Items.Select(i => i.ReferenceNumber).Should().BeEquivalentTo(["PRIO-NUEVO", "PRIO-VIEJO"]);
        // Los más viejos primero, como en todo el detalle de la cola.
        detalle.Items[0].ReferenceNumber.Should().Be("PRIO-VIEJO");
    }

    // ── Trámites por cliente corporativo ─────────────────────────────────────────────────────

    [Fact] // Una fila por empresa con su reparto por familia; la fila suma su total y la tabla se
           // ordena de mayor a menor volumen.
    public async Task ClientesPorFamilia_AgrupaPorEmpresaYFamilia()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            var hace2 = DateTimeOffset.UtcNow.AddDays(-2);
            Radicar(seed, "A-1", TipoMatricula, hace2);
            Radicar(seed, "A-2", TipoMatricula, hace2, hace2.AddHours(5));
            Radicar(seed, "A-3", TipoTraspaso, hace2);
            Radicar(seed, "A-4", TipoOtro, hace2);
            Radicar(seed, "Z-1", TipoTraspaso, hace2, tenantId: OtraEmpresa);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunClientsAsync(db, UltimosDias(7));

        result!.Total.Should().Be(5);
        result.Clientes.Should().HaveCount(2);

        var andina = result.Clientes[0];
        andina.ClientTenantId.Should().Be(ClientTenant);
        andina.Nombre.Should().Be("Flota Andina S.A.S.");
        andina.Matriculas.Should().Be(2);
        andina.Traspasos.Should().Be(1);
        andina.Otros.Should().Be(1);
        andina.Total.Should().Be(4);

        var zeta = result.Clientes[1];
        zeta.Nombre.Should().Be("Transportes Zeta S.A.S.");
        zeta.Traspasos.Should().Be(1);
        zeta.Total.Should().Be(1);
    }

    [Fact] // Mismo universo que el informe: lo radicado fuera del rango no cuenta, y un trámite que
           // volvió a radicarse dentro del rango cuenta una vez.
    public async Task ClientesPorFamilia_UsaElUniversoDelInforme()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "VIEJO", TipoMatricula, DateTimeOffset.UtcNow.AddDays(-20));
            var id = Radicar(seed, "DOS-VECES", TipoMatricula, DateTimeOffset.UtcNow.AddDays(-4),
                DateTimeOffset.UtcNow.AddDays(-3), decisionStatus: TramiteEstado.Rechazado);
            SeedHistory(seed, id, TramiteEstado.Entregado, DateTimeOffset.UtcNow.AddDays(-2));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunClientsAsync(db, UltimosDias(7));
        var report = await RunReportAsync(db, UltimosDias(7));

        result!.Total.Should().Be(1);
        result.Total.Should().Be(report!.Resumen.Total);
        result.Clientes.Should().ContainSingle().Which.Matriculas.Should().Be(1);
    }

    [Fact] // Aislamiento: ni una empresa sin grant vigente ni los trámites de otro organismo entran a
           // la tabla, aunque se hayan radicado en el rango.
    public async Task ClientesPorFamilia_NoIncluyeEmpresasSinGrantNiOtroOrganismo()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            var hace2 = DateTimeOffset.UtcNow.AddDays(-2);
            Radicar(seed, "PROPIO", TipoMatricula, hace2);
            Radicar(seed, "SIN-GRANT", TipoMatricula, hace2, tenantId: SinGrant);
            Radicar(seed, "OTRO-OT", TipoMatricula, hace2, transitOfficeId: OtroOrganismo);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunClientsAsync(db, UltimosDias(7));

        result!.Total.Should().Be(1);
        result.Clientes.Should().ContainSingle().Which.ClientTenantId.Should().Be(ClientTenant);
    }

    [Fact] // Sin nada recibido la tabla viene vacía y en cero, no nula: el Inicio lo dice con palabras.
    public async Task ClientesPorFamilia_SinRecibidosDevuelveVacio()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunClientsAsync(db, UltimosDias(7));

        result.Should().NotBeNull();
        result!.Clientes.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact] // Un tenant sin perfil OT no tiene organismo: null, que el endpoint convierte en 404.
    public async Task ClientesPorFamilia_SinOrganismoDevuelveNull()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunClientsAsync(db, UltimosDias(7));

        result.Should().BeNull();
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────────────────

    private static async Task<OtReportDto?> RunReportAsync(string db, OtMetricsFilter filter)
    {
        await using var ctx = NewContext(db);
        return await new GetOtReportHandler(new OtMetricsReadRepository(ctx)).HandleAsync(
            OtTenant,
            GetOtReportHandler.BuildQuery(filter, 1, OtReportLimits.DefaultPageSize, null, descending: true),
            transitOfficeIdOverride: null,
            TestContext.Current.CancellationToken);
    }

    private static async Task<OtClientsByFamilyDto?> RunClientsAsync(string db, OtMetricsFilter filter)
    {
        await using var ctx = NewContext(db);
        return await new GetOtClientsByFamilyHandler(new OtMetricsReadRepository(ctx)).HandleAsync(
            OtTenant, filter, transitOfficeIdOverride: null, TestContext.Current.CancellationToken);
    }

    private static OtMetricsFilter UltimosDias(int dias) => new(Hoy().AddDays(-(dias - 1)), Hoy());

    // Desfase literal a propósito, igual que OtReportTests: el test es el oráculo del huso.
    private static DateOnly Hoy() => DateOnly.FromDateTime(
        DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    /// <summary>Ayer a las 12:00 de Bogotá: lejos de la medianoche, para que la decisión caiga en un solo día.</summary>
    private static DateTimeOffset AyerAlMediodia()
    {
        var ayer = Hoy().AddDays(-1);
        return new DateTimeOffset(ayer.Year, ayer.Month, ayer.Day, 12, 0, 0, TimeSpan.FromHours(-5));
    }

    private static Guid Radicar(
        FlitDbContext ctx,
        string reference,
        Guid procedureTypeId,
        DateTimeOffset radicadoEn,
        DateTimeOffset? decision = null,
        string decisionStatus = TramiteEstado.Aprobado,
        bool prioritario = false,
        Guid? tenantId = null,
        Guid? transitOfficeId = null)
    {
        var id = Guid.NewGuid();
        var tenant = tenantId ?? ClientTenant;
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = procedureTypeId,
            ReferenceNumber = reference,
            Status = decision is null ? TramiteEstado.Entregado : decisionStatus,
            Prioritario = prioritario,
            TransitOfficeId = transitOfficeId ?? TransitOffice,
            CreatedByUserId = Reviewer,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });

        SeedHistory(ctx, id, TramiteEstado.Entregado, radicadoEn, tenantId: tenant);
        if (decision is DateTimeOffset at)
        {
            SeedHistory(ctx, id, decisionStatus, at, Reviewer, tenant);
        }

        return id;
    }

    private static void SeedHistory(
        FlitDbContext ctx,
        Guid instanceId,
        string toStatus,
        DateTimeOffset at,
        Guid? changedBy = null,
        Guid? tenantId = null) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = null,
            ToStatus = toStatus,
            ChangedAt = at,
            ChangedBy = changedBy,
        });

    private static void SeedScope(FlitDbContext ctx)
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

        foreach (var tenantId in new[] { ClientTenant, OtraEmpresa })
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = TransitOffice,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.Tenants.AddRange(
            NewTenant(ClientTenant, "client", "Flota Andina S.A.S.", "900000000"),
            NewTenant(OtraEmpresa, "zeta", "Transportes Zeta S.A.S.", "900000001"),
            NewTenant(SinGrant, "sin-grant", "Empresa Sin Grant S.A.S.", "900000002"));

        ctx.Users.Add(new User
        {
            Id = Reviewer,
            Email = "ana@organismo.gov.co",
            DisplayName = "Ana Revisora",
        });

        ctx.ProcedureTypes.AddRange(
            NewType(TipoMatricula, "MATRICULAS", "MATRICULA_NUEVA", "Matrícula inicial"),
            NewType(TipoTraspaso, "TRASPASO", "TRASPASO_STANDARD", "Traspaso"),
            NewType(TipoOtro, "OTROS", "RADICACION_CUENTA", "Radicación de cuenta"));

        ctx.SaveChanges();
    }

    private static Tenant NewTenant(Guid id, string code, string name, string taxId) => new()
    {
        Id = id,
        Code = code,
        LegalName = name,
        TaxId = taxId,
        TenantType = "client",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureType NewType(Guid id, string family, string code, string name) => new()
    {
        Id = id,
        Family = family,
        Code = code,
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
