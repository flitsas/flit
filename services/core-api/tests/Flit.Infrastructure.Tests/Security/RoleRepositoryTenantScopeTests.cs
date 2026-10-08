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
