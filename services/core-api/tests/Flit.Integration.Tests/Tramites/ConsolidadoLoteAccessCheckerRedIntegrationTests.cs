using System.Data.Common;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13418 (épica #13216, ADR-0070 adenda v7 A7.4, CF-R5) — rama de RED de <see cref="ConsolidadoLoteAccessChecker"/>
/// contra PostgreSQL real sobre <see cref="HierarchyScenario"/> (P cabeza con hijas C1 y C2; X ajeno). El solicitante es
/// el usuario de P con un rol que otorga <c>consolidado-masivo.download</c> y el rol <c>AdminCompany</c>, ambos en P y
/// leídos de la BD (no del JWT).
/// <list type="bullet">
///   <item>AC4 — C1 deja de ser hija de P ⇒ sus ítems pierden el acceso (consulta por ítem, sin caché); P y C2 siguen.</item>
///   <item>AC5 — el solicitante pierde <c>AdminCompany</c> activo en P ⇒ los ítems de las hijas pierden el acceso.</item>
///   <item>AC6 — <c>group_read_scope</c> apagado, o <c>network_documents_concesion</c> apagado en una cabeza CONCESIÓN
///   ⇒ los ítems de las hijas pierden el acceso (como mucho 60 s después: caché por lote).</item>
///   <item>AC7 — lote acotado a C1 ⇒ un ítem de otra compañía (C2 o la propia P) no tiene acceso.</item>
///   <item>AC8 — la lectura de la jerarquía falla por un error de BD ⇒ la excepción se propaga (fallo técnico que el
///   handler reprograma) y no se cachea como acceso revocado.</item>
/// </list>
/// <para>Uso de ejemplo: <c>await new ConsolidadoLoteAccessChecker(ctx, cache).TieneAccesoAsync(Ctx(C1, lote), ct)</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteAccessCheckerRedIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid P = HierarchyScenario.P;
    private static readonly Guid C1 = HierarchyScenario.C1;
    private static readonly Guid C2 = HierarchyScenario.C2;
    private static readonly Guid X = HierarchyScenario.X;
    private static readonly Guid Solicitante = HierarchyScenario.UserOf(P);

    private static readonly Guid RolDescarga = new("e1000000-0000-4000-8000-000000013418");
    private static readonly Guid RolAdminCompany = new("e2000000-0000-4000-8000-000000013418");
    private static readonly Guid AsignacionAdmin = new("e3000000-0000-4000-8000-000000013418");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── base ──────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Base_RedVigente_ItemsDeP_C1_YC2_ConAcceso_YElDeUnAjenoSin()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        using var cache = NewCache();
        var lote = Guid.NewGuid();

        (await CheckAsync(cache, Ctx(P, lote))).Should().BeTrue("ítem propio de la cabeza");
        (await CheckAsync(cache, Ctx(C1, lote))).Should().BeTrue("hija vigente");
        (await CheckAsync(cache, Ctx(C2, lote))).Should().BeTrue("hija vigente");
        (await CheckAsync(cache, Ctx(X, lote))).Should().BeFalse("X no es hija de P: un id inyectado no pasa");
    }

    // ── AC4 — la hija sale de la red ──────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_C1DejaDeSerHijaDeP_SusItemsPierdenElAcceso_LosDePYC2Siguen()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        using var cache = NewCache();
        var lote = Guid.NewGuid();
        (await CheckAsync(cache, Ctx(C1, lote))).Should().BeTrue();

        await using (var ctx = NewContext())
        {
            var c1 = await ctx.Tenants.SingleAsync(t => t.Id == C1, Ct);
            c1.ParentTenantId = null;
            await ctx.SaveChangesAsync(Ct);
        }

        (await CheckAsync(cache, Ctx(C1, lote))).Should().BeFalse("la jerarquía se consulta en cada ítem (≤ 60 s)");
        (await CheckAsync(cache, Ctx(P, lote))).Should().BeTrue("los de P siguen");
        (await CheckAsync(cache, Ctx(C2, lote))).Should().BeTrue("los de C2 siguen");
    }

    // ── AC5 — pérdida del rol de red ──────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_PierdeAdminCompanyActivoEnP_LosItemsDeLasHijasPierdenElAcceso_LosDePNo()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        await using (var ctx = NewContext())
        {
            var asignacion = await ctx.UserRoleAssignments.SingleAsync(a => a.Id == AsignacionAdmin, Ct);
            asignacion.DeletedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync(Ct);
        }

        using var cache = NewCache();
        var lote = Guid.NewGuid();
        (await CheckAsync(cache, Ctx(C1, lote))).Should().BeFalse("el rol de red se lee de la BD, no del JWT");
        (await CheckAsync(cache, Ctx(C2, lote))).Should().BeFalse();
        (await CheckAsync(cache, Ctx(P, lote))).Should().BeTrue("conserva el permiso de descarga en P por otro rol");
    }

    [PostgresFact]
    public async Task AC5_Edge_RolAdminCompanyDesactivado_OSuspensionVigenteEnP_SinAccesoALasHijas()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        await using (var ctx = NewContext())
        {
            var rol = await ctx.Roles.SingleAsync(r => r.Id == RolAdminCompany, Ct);
            rol.IsActive = false;
            await ctx.SaveChangesAsync(Ct);
        }

        using (var cache = NewCache())
            (await CheckAsync(cache, Ctx(C1, Guid.NewGuid()))).Should().BeFalse("rol AdminCompany inactivo");

        await using (var ctx = NewContext())
        {
            (await ctx.Roles.SingleAsync(r => r.Id == RolAdminCompany, Ct)).IsActive = true;
            ctx.UserTempSuspensions.Add(new UserTempSuspension
            {
                Id = Guid.NewGuid(),
                TenantId = P,
                UserId = Solicitante,
                StartsAt = DateTimeOffset.UtcNow.AddHours(-1),
                EndsAt = DateTimeOffset.UtcNow.AddDays(1),
                Reason = "HU #13418 (test)",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        using (var cache = NewCache())
            (await CheckAsync(cache, Ctx(C1, Guid.NewGuid()))).Should().BeFalse("suspensión vigente en P (VigenteEn)");
    }

    // ── AC6 — interruptores ───────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_GroupReadScopeApagado_LosItemsDeLasHijasPierdenElAcceso_ComoMuchoAlVencerLaCache()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        using var cache = NewCache();
        var lote = Guid.NewGuid();
        (await CheckAsync(cache, Ctx(C1, lote))).Should().BeTrue();

        await SetSwitchAsync(HierarchySwitch.GroupReadScopeKey, false);

        (await CheckAsync(cache, Ctx(C1, lote))).Should().BeTrue("dentro de los 60 s la decisión de red sale de la caché del lote");
        using var vencida = NewCache(); // la caché del lote vencida (60 s)
        (await CheckAsync(vencida, Ctx(C1, lote))).Should().BeFalse("interruptor global apagado");
        (await CheckAsync(vencida, Ctx(C2, lote))).Should().BeFalse();
        (await CheckAsync(vencida, Ctx(P, lote))).Should().BeTrue("el ítem propio de P no depende de la red");
        ConsolidadoLoteAccessChecker.DuracionCache.Should().Be(TimeSpan.FromSeconds(60));
    }

    [PostgresFact]
    public async Task AC6_CabezaConcesion_ConDocumentosDeRedApagados_SinAccesoALasHijas_YEncendidos_ConAcceso()
    {
        await SeedAsync(GroupKindCodes.Concesion);
        await SetSwitchAsync(HierarchySwitch.NetworkDocumentsConcesionKey, false);

        using (var cache = NewCache())
        {
            (await CheckAsync(cache, Ctx(C1, Guid.NewGuid()))).Should().BeFalse("P2 = a: CONCESIÓN con el interruptor apagado");
            (await CheckAsync(cache, Ctx(P, Guid.NewGuid()))).Should().BeTrue();
        }

        await SetSwitchAsync(HierarchySwitch.NetworkDocumentsConcesionKey, true);
        using (var cache = NewCache())
            (await CheckAsync(cache, Ctx(C1, Guid.NewGuid()))).Should().BeTrue("encendido se comporta como MARCA_BLANCA");
    }

    [PostgresFact]
    public async Task AC6_Edge_CabezaMarcaBlanca_NoDependeDelInterruptorDeConcesion()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        await SetSwitchAsync(HierarchySwitch.NetworkDocumentsConcesionKey, false);

        using var cache = NewCache();
        (await CheckAsync(cache, Ctx(C1, Guid.NewGuid()))).Should().BeTrue();
    }

    // ── AC7 — lote acotado a una hija ─────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC7_LoteAcotadoAC1_ItemDeOtraCompania_SinAcceso()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        using var cache = NewCache();
        var lote = Guid.NewGuid();

        (await CheckAsync(cache, Ctx(C1, lote, scope: C1))).Should().BeTrue();
        (await CheckAsync(cache, Ctx(C2, lote, scope: C1))).Should().BeFalse("C2 no es la hija acotada");
        (await CheckAsync(cache, Ctx(P, lote, scope: C1))).Should().BeFalse("ni siquiera la propia cabeza");
    }

    // ── AC8 — error de BD ─────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_ErrorDeBdAlLeerLaJerarquia_SePropaga_YNoSeCacheaComoAccesoRevocado()
    {
        await SeedAsync(GroupKindCodes.MarcaBlanca);
        using var cache = NewCache();
        var lote = Guid.NewGuid();

        await using (var roto = ContextoQueFallaEn("hierarchy_switches", "tenants"))
        {
            var act = () => new ConsolidadoLoteAccessChecker(roto, cache).TieneAccesoAsync(Ctx(C1, lote), Ct);
            await act.Should().ThrowAsync<NpgsqlException>("un fallo de BD es fallo técnico (reintento), no «acceso revocado»");
        }

        (await CheckAsync(cache, Ctx(C1, lote))).Should().BeTrue("el error no quedó cacheado como revocación del lote");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Escenario canónico con la clase de cabeza pedida + RBAC del solicitante en P: un rol con el permiso de descarga
    /// (sin ser AdminCompany) y el rol <c>AdminCompany</c> (sin el permiso), para separar «permiso» de «rol de red».
    /// </summary>
    private async Task SeedAsync(string headKind)
    {
        await HierarchyScenario.SeedAsync(Fixture, headTenantType: headKind);
        await using var ctx = NewContext();
        var accion = await ctx.RbacActions.FirstOrDefaultAsync(a => a.Slug == ConsolidadoLotePermisos.Descargar, Ct);
        if (accion is null)
        {
            var modulo = new SecurityModule { Id = Guid.NewGuid(), Code = "it-13418", Name = "IT 13418", CreatedAt = DateTimeOffset.UtcNow };
            ctx.SecurityModules.Add(modulo);
            accion = new RbacAction
            {
                Id = Guid.NewGuid(),
                ModuleId = modulo.Id,
                Slug = ConsolidadoLotePermisos.Descargar,
                Name = "Descargar",
                HttpMethod = "POST",
                RoutePattern = "/api/v1/tramites/consolidados/lotes",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            ctx.RbacActions.Add(accion);
        }

        ctx.Roles.AddRange(
            new Role { Id = RolDescarga, Code = "it_13418_gestor", Name = "IT 13418 gestor", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new Role
            {
                // Como en producción: AdminCompany es del producto plataforma y el rol de descarga, de trámites
                // (uq_ura_active_user_tenant_product: un rol activo por usuario, empresa y producto).
                Id = RolAdminCompany, Code = "AdminCompany", Name = "Administrador de compañía", ProductCode = "plataforma",
                IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            });
        ctx.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), RoleId = RolDescarga, PermissionId = accion.Id, CreatedAt = DateTimeOffset.UtcNow });
        ctx.UserRoleAssignments.AddRange(
            Asignacion(Guid.NewGuid(), RolDescarga),
            Asignacion(AsignacionAdmin, RolAdminCompany));
        await ctx.SaveChangesAsync(Ct);
    }

    private static UserRoleAssignment Asignacion(Guid id, Guid rol) => new()
    {
        Id = id,
        UserId = Solicitante,
        RoleId = rol,
        TenantId = P,
        AssignedAt = DateTimeOffset.UtcNow.AddDays(-1),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private async Task SetSwitchAsync(string key, bool encendido)
    {
        await using var ctx = NewContext();
        var fila = await ctx.HierarchySwitches.SingleAsync(s => s.SwitchKey == key, Ct);
        fila.IsEnabled = encendido;
        fila.UpdatedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(Ct);
    }

    /// <summary>Ítem del trámite entregado de <paramref name="compania"/> en un lote de red de P.</summary>
    private static LoteItemContexto Ctx(Guid compania, Guid lote, Guid? scope = null) =>
        new(lote, Guid.NewGuid(), ConsolidadoExportOrigin.Tramites, P, compania, null, Solicitante, "AdminCompany",
            HierarchyScenario.DeliveredProcedureOf(compania), ConsolidadoExportDocumentType.Consolidado,
            RedActiva: true, ScopeTenantId: scope);

    private async Task<bool> CheckAsync(IMemoryCache cache, LoteItemContexto contexto)
    {
        await using var ctx = NewContext();
        return await new ConsolidadoLoteAccessChecker(ctx, cache).TieneAccesoAsync(contexto, Ct);
    }

    private static MemoryCache NewCache() => new(new MemoryCacheOptions());

    /// <summary>Contexto real cuyas lecturas sobre las tablas indicadas fallan como lo haría una BD caída.</summary>
    private FlitDbContext ContextoQueFallaEn(params string[] tablas) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(new FalloEnTablas(tablas))
            .Options);

    private sealed class FalloEnTablas(string[] tablas) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            tablas.Any(t => Regex.IsMatch(command.CommandText, @"\b" + t + @"\b"))
                ? throw new NpgsqlException("fallo simulado de la BD (HU #13418 AC8)")
                : base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
