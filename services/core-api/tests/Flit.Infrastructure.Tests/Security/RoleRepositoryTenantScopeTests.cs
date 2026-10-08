using Flit.Api.Endpoints;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13441 — el alcance por tenant de <see cref="RoleRepository"/> y la traducción de errores a HTTP.
/// Corre sobre InMemory: la RLS de la tabla es nominal (la app es owner), así que lo que aísla es este filtro.
/// </summary>
public sealed class RoleRepositoryTenantScopeTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static (FlitDbContext Db, RoleRepository Repo) Build()
    {
        var db = new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new RoleRepository(db));
    }

    private static Role NewRole(string code, Guid? tenant, string target = "COMPANY", bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        TargetEntityType = target,
        TenantId = tenant,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
    };

    [Fact]
    public async Task ListVisibleToTenant_DevuelveGlobalesYPropiosNuncaLosDeOtroTenant()
    {
        var (db, repo) = Build();
        db.Roles.AddRange(NewRole("AdminCompany", null), NewRole("contador", TenantA), NewRole("auditor", TenantB),
            NewRole("ot_admin", null, "TRANSIT_OFFICE"), NewRole("borrado", TenantA, deleted: true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var a = await repo.ListVisibleToTenantAsync(TenantA, "COMPANY", TestContext.Current.CancellationToken);
        var b = await repo.ListVisibleToTenantAsync(TenantB, "COMPANY", TestContext.Current.CancellationToken);

        a.Select(r => r.Code).Should().BeEquivalentTo("AdminCompany", "contador");
        b.Select(r => r.Code).Should().BeEquivalentTo("AdminCompany", "auditor");
        a.Single(r => r.Code == "contador").TenantId.Should().Be(TenantA);
        a.Single(r => r.Code == "AdminCompany").TenantId.Should().BeNull();
    }

    [Fact]
    public async Task GetVisibleToTenant_NoRevelaElRolDeOtroTenant()
    {
        var (db, repo) = Build();
        var propio = NewRole("contador", TenantA);
        var ajeno = NewRole("auditor", TenantB);
        var global = NewRole("AdminCompany", null);
        db.Roles.AddRange(propio, ajeno, global);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await repo.GetVisibleToTenantAsync(TenantA, propio.Id, TestContext.Current.CancellationToken))!.TenantId.Should().Be(TenantA);
        (await repo.GetVisibleToTenantAsync(TenantA, global.Id, TestContext.Current.CancellationToken))!.TenantId.Should().BeNull();
        (await repo.GetVisibleToTenantAsync(TenantA, ajeno.Id, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task CodeTakenForTenant_ChocaConGlobalesYPropiosPeroNoConOtroTenantNiBorrados()
    {
        var (db, repo) = Build();
        db.Roles.AddRange(NewRole("AdminCompany", null), NewRole("contador", TenantA), NewRole("auditor", TenantB),
            NewRole("viejo", TenantA, deleted: true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        (await repo.CodeTakenForTenantAsync(TenantA, "AdminCompany", ct)).Should().BeTrue("repite un code global");
        (await repo.CodeTakenForTenantAsync(TenantA, "contador", ct)).Should().BeTrue("repite uno propio");
        (await repo.CodeTakenForTenantAsync(TenantA, "auditor", ct)).Should().BeFalse("es de otra compañía: se permite");
        (await repo.CodeTakenForTenantAsync(TenantA, "viejo", ct)).Should().BeFalse("el rol borrado libera el code");
    }

    [Fact]
    public async Task CreateAsync_GuardaElTenantDelRol()
    {
        var (db, repo) = Build();

        var id = await repo.CreateAsync(
            new CreateRoleData("COMPANY", "contador", "Contador", null, "tramites", TenantA), TestContext.Current.CancellationToken);

        (await db.Roles.FindAsync([id], TestContext.Current.CancellationToken))!.TenantId.Should().Be(TenantA);
    }

    [Fact]
    public async Task GetEnabledProductCodes_SiempreIncluyePlataformaYSoloLosEncendidosDelTenant()
    {
        var (db, repo) = Build();
        db.Set<TenantProductEntity>().AddRange(
            new TenantProductEntity { Id = Guid.NewGuid(), TenantId = TenantA, ProductCode = "tramites", Enabled = true },
            new TenantProductEntity { Id = Guid.NewGuid(), TenantId = TenantA, ProductCode = "comparendos", Enabled = false },
            new TenantProductEntity { Id = Guid.NewGuid(), TenantId = TenantB, ProductCode = "comparendos", Enabled = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var enabled = await repo.GetEnabledProductCodesAsync(TenantA, TestContext.Current.CancellationToken);

        enabled.Should().BeEquivalentTo("plataforma", "tramites");
    }

    // ── Correcciones de revisión (HU #13441) ───────────────────────────────────────────────────────

    private static DbUpdateException Violation23505(string? constraint, string message = "duplicate key") =>
        new("save", new PostgresException(message, "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, constraintName: constraint));

    [Theory]
    [InlineData("uq_roles_tenant_code")]
    [InlineData("uq_roles_code_target_entity_type")]
    public void IsRoleCodeViolation_ReconoceLosIndicesUnicosDeCode(string constraint)
    {
        RoleRepository.IsRoleCodeViolation(Violation23505(constraint)).Should().BeTrue();
    }

    [Fact]
    public void IsRoleCodeViolation_ReconoceElTriggerDeCodeGlobal()
    {
        RoleRepository.IsRoleCodeViolation(Violation23505(null, "ROLE_CODE_DUPLICATE: el code x ya existe como rol global"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsRoleCodeViolation_IgnoraOtrosErrores()
    {
        RoleRepository.IsRoleCodeViolation(new DbUpdateException("x", new PostgresException("boom", "ERROR", "ERROR", "22001")))
            .Should().BeFalse();
        RoleRepository.IsRoleCodeViolation(new DbUpdateException("x")).Should().BeFalse();
    }

    private sealed class ThrowOnSave(Exception ex) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw ex;
    }

    // Carrera, o SuperAdmin creando un rol global con el code de un rol de tenant: el 23505 llega como excepción de dominio
    [Fact]
    public async Task CreateAsync_Con23505_LanzaRoleCodeDuplicateYNoDejaLaFilaPendiente()
    {
        var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowOnSave(Violation23505("uq_roles_tenant_code")))
            .Options);
        var repo = new RoleRepository(db);

        var act = () => repo.CreateAsync(
            new CreateRoleData("COMPANY", "contador", "Contador", null, "tramites", TenantA), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RoleCodeDuplicateException>();
        db.ChangeTracker.Entries<Role>().Should().BeEmpty("la entidad rechazada se desprende del contexto");
    }

    [Fact]
    public async Task CodeTakenForTenant_EsCaseInsensitive()
    {
        var (db, repo) = Build();
        db.Roles.AddRange(NewRole("AdminCompany", null), NewRole("Contador", TenantA));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        (await repo.CodeTakenForTenantAsync(TenantB, "admincompany", ct)).Should().BeTrue();
        (await repo.CodeTakenForTenantAsync(TenantA, "CONTADOR", ct)).Should().BeTrue();
        (await repo.CodeTakenForTenantAsync(TenantB, "contador", ct)).Should().BeFalse();
    }

    private static UserInvitation Invitation(Guid? roleId, string status, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantA,
        Email = "a@b.c",
        FullName = "A",
        RoleId = roleId,
        TokenHash = Guid.NewGuid().ToString("N"),
        Status = status,
        InvitedBy = Guid.NewGuid(),
        DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
    };

    // Un rol con invitación pendiente no se puede eliminar (409)
    [Fact]
    public async Task HasActiveUsers_CuentaInvitacionesPendientesPrimariasYSecundarias()
    {
        var (db, repo) = Build();
        var primario = NewRole("p", TenantA);
        var secundario = NewRole("s", TenantA);
        var libre = NewRole("l", TenantA);
        var aceptado = NewRole("a", TenantA);
        var pendiente = Invitation(primario.Id, "pending");
        var otra = Invitation(null, "pending");
        var aceptada = Invitation(aceptado.Id, "accepted");
        db.Roles.AddRange(primario, secundario, libre, aceptado);
        db.UserInvitations.AddRange(pendiente, otra, aceptada);
        db.InvitationRoles.Add(new InvitationRole { Id = Guid.NewGuid(), TenantId = TenantA, InvitationId = otra.Id, RoleId = secundario.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        (await repo.HasActiveUsersAsync(primario.Id, ct)).Should().BeTrue("invitación pendiente con rol primario");
        (await repo.HasActiveUsersAsync(secundario.Id, ct)).Should().BeTrue("invitación pendiente con el rol en invitation_roles");
        (await repo.HasActiveUsersAsync(aceptado.Id, ct)).Should().BeFalse("la invitación ya fue aceptada");
        (await repo.HasActiveUsersAsync(libre.Id, ct)).Should().BeFalse();
    }

    // GetPermissionInfos no devuelve permisos de módulos borrados (igual que ListGrantable)
    [Fact]
    public async Task GetPermissionInfos_ExcluyePermisosDeModulosBorrados()
    {
        var (db, repo) = Build();
        var vivo = new SecurityModule { Id = Guid.NewGuid(), Code = "vivo", Name = "v", ProductCode = "tramites", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var borrado = new SecurityModule { Id = Guid.NewGuid(), Code = "muerto", Name = "m", ProductCode = "tramites", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, DeletedAt = DateTimeOffset.UtcNow };
        var a = new RbacAction { Id = Guid.NewGuid(), ModuleId = vivo.Id, Slug = "vivo.read", Name = "r", HttpMethod = "GET", RoutePattern = "/x", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var b = new RbacAction { Id = Guid.NewGuid(), ModuleId = borrado.Id, Slug = "muerto.read", Name = "r", HttpMethod = "GET", RoutePattern = "/y", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        db.SecurityModules.AddRange(vivo, borrado);
        db.RbacActions.AddRange(a, b);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var infos = await repo.GetPermissionInfosAsync([a.Id, b.Id], TestContext.Current.CancellationToken);

        infos.Select(i => i.Slug).Should().Equal("vivo.read");
    }

    [Theory]
    [MemberData(nameof(Errores))]
    public void TenantRoleErrors_TraduceCadaExcepcionAlStatusEsperado(Exception ex, int status)
    {
        var result = TenantRoleErrors.TryMap(ex);

        result.Should().NotBeNull();
        (result as IStatusCodeHttpResult)!.StatusCode.Should().Be(status);
    }

    public static TheoryData<Exception, int> Errores => new()
    {
        { new InvalidTargetEntityTypeException(), StatusCodes.Status400BadRequest },
        { new InvalidRoleInputException(), StatusCodes.Status400BadRequest },
        { new PrivilegeCeilingException(PrivilegeCeilingCodes.PlatformOnly, ["banners.manage"]), StatusCodes.Status403Forbidden },
        { new RoleNotOwnedException(), StatusCodes.Status403Forbidden },
        { new RoleNotFoundException(), StatusCodes.Status404NotFound },
        { new RoleHasActiveUsersException(), StatusCodes.Status409Conflict },
        { new RoleCodeDuplicateException(), StatusCodes.Status409Conflict },
    };

    [Fact]
    public void TenantRoleErrors_ExcepcionDesconocida_NoSeTraduce()
    {
        TenantRoleErrors.TryMap(new InvalidOperationException()).Should().BeNull();
    }
}
