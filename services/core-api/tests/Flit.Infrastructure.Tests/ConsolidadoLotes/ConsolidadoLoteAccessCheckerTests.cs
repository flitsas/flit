using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13375 (CF-16, AC2 y AC6) — <see cref="ConsolidadoLoteAccessChecker"/> sobre el modelo RBAC real de
/// <see cref="FlitDbContext"/> (InMemory): usuario activo, membresía activa en la compañía CONGELADA del lote con un
/// rol activo que otorgue <c>consolidado-masivo.download</c>, trámite vivo de esa compañía; Super Admin con rol activo;
/// caché de 60 s por lote.
/// <para>Uso de ejemplo:
/// <c>await new ConsolidadoLoteAccessChecker(db, cache).TieneAccesoAsync(LoteItemContexto.Desde(lote, item), ct)</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteAccessCheckerTests : IDisposable
{
    private static readonly Guid CompaniaC = Guid.NewGuid();
    private static readonly Guid CompaniaD = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid RolRadicador = Guid.NewGuid();
    private static readonly Guid RolSinPermiso = Guid.NewGuid();
    private static readonly Guid RolSuperAdmin = Guid.NewGuid();
    private static readonly Guid Permiso = Guid.NewGuid();

    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    private FlitDbContext Db() =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task<FlitDbContext> SeedAsync(string status = "active")
    {
        var db = Db();
        db.Users.Add(new User { Id = Usuario, Email = "lote13375@it.test", DisplayName = "Radicador", Status = status });
        db.Roles.AddRange(
            new Role { Id = RolRadicador, Code = "Radicador", Name = "Radicador", IsActive = true },
            new Role { Id = RolSinPermiso, Code = "Consulta", Name = "Consulta", IsActive = true },
            new Role { Id = RolSuperAdmin, Code = "SuperAdmin", Name = "Super Admin", IsActive = true, ProductCode = "plataforma" });
        db.RbacActions.Add(new RbacAction { Id = Permiso, ModuleId = Guid.NewGuid(), Slug = ConsolidadoLotePermisos.Descargar, Name = "Descargar", IsActive = true });
        db.RoleGrants.AddRange(
            new RoleGrant { Id = Guid.NewGuid(), RoleId = RolRadicador, PermissionId = Permiso },
            new RoleGrant { Id = Guid.NewGuid(), RoleId = RolSuperAdmin, PermissionId = Permiso });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static UserRoleAssignment Asignacion(Guid rol, Guid compania, DateTimeOffset? borrada = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Usuario,
        RoleId = rol,
        TenantId = compania,
        AssignedAt = DateTimeOffset.UtcNow.AddDays(-10),
        DeletedAt = borrada,
    };

    private static ProcedureInstance Tramite(Guid compania, DateTimeOffset? borrado = null) => new()
    {
        ProcedureType = ProcedureTypeFixture.Matricula,
        Id = Guid.NewGuid(),
        TenantId = compania,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-13375",
        CreatedAt = DateTimeOffset.UtcNow,
        DeletedAt = borrado,
    };

    private static LoteItemContexto Ctx(
        ProcedureInstance tramite, Guid? companiaLote, string origen = ConsolidadoExportOrigin.Tramites, Guid? lote = null) =>
        new(lote ?? Guid.NewGuid(), Guid.NewGuid(), origen, companiaLote, tramite.TenantId, null, Usuario,
            origen == ConsolidadoExportOrigin.Superadmin ? "SuperAdmin" : "Radicador", tramite.Id, "consolidado");

    private async Task<bool> CheckAsync(FlitDbContext db, LoteItemContexto ctx) =>
        await new ConsolidadoLoteAccessChecker(db, _cache).TieneAccesoAsync(ctx, TestContext.Current.CancellationToken);

    // ── tramites: membresía en la compañía congelada ────────────────────────────────────────────

    [Fact]
    public async Task AC6_MembresiaActivaConRolQueOtorgaElPermiso_EnLaCompaniaDelLote_TieneAcceso()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaC);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaC));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeTrue();
    }

    [Fact]
    public async Task AC6_MembresiaEnOtraCompania_NoCuenta_SeVerificaContraLaDelLote()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaC);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaD));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeFalse();
    }

    [Fact]
    public async Task AC2_RolRetirado_DespuesDeCrearElLote_PierdeAcceso()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaC);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaC, borrada: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeFalse();
    }

    [Fact]
    public async Task AC2_RolSinElPermiso_RolDesactivado_OPermisoDesactivado_NoTienenAcceso()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaC);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolSinPermiso, CompaniaC));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeFalse("el rol no otorga el permiso");

        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaC));
        (await db.Roles.SingleAsync(r => r.Id == RolRadicador, TestContext.Current.CancellationToken)).IsActive = false;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeFalse("rol desactivado");

        (await db.Roles.SingleAsync(r => r.Id == RolRadicador, TestContext.Current.CancellationToken)).IsActive = true;
        (await db.RbacActions.SingleAsync(a => a.Id == Permiso, TestContext.Current.CancellationToken)).IsActive = false;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeFalse("permiso desactivado");
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("pending")]
    public async Task AC2_UsuarioNoActivo_PierdeAcceso(string status)
    {
        await using var db = await SeedAsync(status);
        var t = Tramite(CompaniaC);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaC));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeFalse();
    }

    [Fact]
    public async Task AC2_TramiteBorrado_OTramiteDeOtraCompania_PierdeAcceso()
    {
        await using var db = await SeedAsync();
        var borrado = Tramite(CompaniaC, borrado: DateTimeOffset.UtcNow);
        var ajeno = Tramite(CompaniaD);
        db.ProcedureInstances.AddRange(borrado, ajeno);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaC));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(borrado, CompaniaC))).Should().BeFalse("borrado lógico");
        (await CheckAsync(db, Ctx(ajeno, CompaniaC))).Should().BeFalse("el trámite no es de la compañía congelada");
    }

    // ── superadmin ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_LoteDeSuperAdmin_BastaUsuarioActivoConRolSuperAdminActivo_YTramiteVivo()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaD);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolSuperAdmin, CompaniaC)); // asignado en otra compañía
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, null, ConsolidadoExportOrigin.Superadmin))).Should().BeTrue();
    }

    [Fact]
    public async Task AC6_LoteDeSuperAdmin_SinRolSuperAdminActivo_OTramiteBorrado_NoTieneAcceso()
    {
        await using var db = await SeedAsync();
        var vivo = Tramite(CompaniaD);
        var borrado = Tramite(CompaniaD, borrado: DateTimeOffset.UtcNow);
        db.ProcedureInstances.AddRange(vivo, borrado);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaD)); // tiene el permiso, pero no es SuperAdmin
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await CheckAsync(db, Ctx(vivo, null, ConsolidadoExportOrigin.Superadmin))).Should().BeFalse();

        db.UserRoleAssignments.Add(Asignacion(RolSuperAdmin, CompaniaC));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await CheckAsync(db, Ctx(borrado, null, ConsolidadoExportOrigin.Superadmin))).Should().BeFalse();
    }

    // ── AC7: suspensión temporal del solicitante ──────────────────────────────────────────────

    private static UserTempSuspension Suspension(Guid compania, DateTimeOffset inicio, DateTimeOffset? fin, DateTimeOffset? levantada = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = compania,
        UserId = Usuario,
        StartsAt = inicio,
        EndsAt = fin,
        Reason = "HU #13375 AC7 (test)",
        CreatedAt = DateTimeOffset.UtcNow,
        DeletedAt = levantada,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC7_SuspensionVigente_EnLaCompaniaDelLote_PierdeAcceso(bool indefinida)
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaC);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaC));
        db.UserTempSuspensions.Add(Suspension(CompaniaC, DateTimeOffset.UtcNow.AddHours(-1), indefinida ? null : DateTimeOffset.UtcNow.AddDays(1)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeFalse("el login ya bloquea al suspendido; el lote tampoco sigue");
    }

    [Fact]
    public async Task AC7_SuspensionVencida_Levantada_NoIniciada_OEnOtraCompania_ConservaAcceso()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaC);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolRadicador, CompaniaC));
        db.UserTempSuspensions.AddRange(
            Suspension(CompaniaC, DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-1)),
            Suspension(CompaniaC, DateTimeOffset.UtcNow.AddHours(-1), null, levantada: DateTimeOffset.UtcNow.AddMinutes(-5)),
            Suspension(CompaniaC, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2)),
            Suspension(CompaniaD, DateTimeOffset.UtcNow.AddHours(-1), null));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, CompaniaC))).Should().BeTrue("ninguna suspensión está vigente en la compañía del lote");
    }

    [Fact]
    public async Task AC7_LoteDeSuperAdmin_SuspendidoDondeTieneElRolSuperAdmin_PierdeAcceso_YVencida_LoRecupera()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaD);
        db.ProcedureInstances.Add(t);
        db.UserRoleAssignments.Add(Asignacion(RolSuperAdmin, CompaniaC));
        var suspension = Suspension(CompaniaC, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        db.UserTempSuspensions.Add(suspension);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t, null, ConsolidadoExportOrigin.Superadmin))).Should().BeFalse();

        suspension.EndsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await CheckAsync(db, Ctx(t, null, ConsolidadoExportOrigin.Superadmin))).Should().BeTrue("vencida: otro lote, sin caché");
    }

    // ── caché y contrato ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cache_LaDecisionDelSolicitanteSeCacheaPorLote_ElTramiteSeRevalidaSiempre()
    {
        await using var db = await SeedAsync();
        var t1 = Tramite(CompaniaC);
        var t2 = Tramite(CompaniaC);
        db.ProcedureInstances.AddRange(t1, t2);
        var asignacion = Asignacion(RolRadicador, CompaniaC);
        db.UserRoleAssignments.Add(asignacion);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var lote = Guid.NewGuid();

        (await CheckAsync(db, Ctx(t1, CompaniaC, lote: lote))).Should().BeTrue();

        asignacion.DeletedAt = DateTimeOffset.UtcNow;
        t2.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(db, Ctx(t1, CompaniaC, lote: lote))).Should().BeTrue("dentro de los 60 s la membresía sale de la caché del lote");
        (await CheckAsync(db, Ctx(t2, CompaniaC, lote: lote))).Should().BeFalse("el trámite vivo se comprueba en cada ítem");
        (await CheckAsync(db, Ctx(t1, CompaniaC, lote: Guid.NewGuid()))).Should().BeFalse("otro lote no comparte la caché");
        ConsolidadoLoteAccessChecker.DuracionCache.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Contrato_OrigenOtBandeja_NoLoAtiendeEsteChecker()
    {
        await using var db = await SeedAsync();
        var t = Tramite(CompaniaC);

        var act = () => CheckAsync(db, Ctx(t, CompaniaC, ConsolidadoExportOrigin.OtBandeja));

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
