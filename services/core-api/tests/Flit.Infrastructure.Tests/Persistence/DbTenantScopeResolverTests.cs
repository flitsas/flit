using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12321 (Feature #12254) — <see cref="DbTenantScopeResolver"/> sobre <c>identity.tenants</c>
/// (InMemory). Uso de ejemplo:
/// <code>
/// var resolver = new DbTenantScopeResolver(db, NullLogger&lt;DbTenantScopeResolver&gt;.Instance);
/// var scope = await resolver.ResolveAsync(tenantId, ct); // Single o Group, nunca All
/// </code>
/// </summary>
public sealed class DbTenantScopeResolverTests
{
    private static readonly Guid Parent = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Child1 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Child2 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Lonely = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static FlitDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(name).Options);

    private static Tenant Row(Guid id, bool isGroupParent = false, Guid? parent = null, bool isActive = true) => new()
    {
        Id = id,
        Code = id.ToString("N")[..8],
        LegalName = "T " + id.ToString("N")[..8],
        TaxId = id.ToString("N")[..9],
        TenantType = "CONCESIONARIO",
        IsActive = isActive,
        IsGroupParent = isGroupParent,
        ParentTenantId = parent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static DbTenantScopeResolver Sut(FlitDbContext db) =>
        new(db, NullLogger<DbTenantScopeResolver>.Instance);

    // ── AC1 — cliente sin jerarquía ────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_TenantSinPadreNiHijos_DevuelveSingle()
    {
        await using var db = NewDb(nameof(ResolveAsync_TenantSinPadreNiHijos_DevuelveSingle));
        db.Tenants.Add(Row(Lonely));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Lonely, TestContext.Current.CancellationToken);

        scope.IsAll.Should().BeFalse();
        scope.IsGroup.Should().BeFalse();
        scope.WriteTenantId.Should().Be(Lonely);
        scope.ReadTenantIds.Should().BeEquivalentTo([Lonely]);
    }

    [Fact]
    public async Task ResolveAsync_HijoDeUnGrupo_DevuelveSingleDelHijo_NoVeAlPadre()
    {
        await using var db = NewDb(nameof(ResolveAsync_HijoDeUnGrupo_DevuelveSingleDelHijo_NoVeAlPadre));
        db.Tenants.AddRange(Row(Parent, isGroupParent: true), Row(Child1, parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Child1, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([Child1]);
        scope.CanRead(Parent).Should().BeFalse("la jerarquía es descendente: el hijo no ve al padre");
    }

    // ── AC2 — cabeza de grupo con dos hijos ────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CabezaConDosHijos_DevuelveGroupConPadreYAmbosHijos()
    {
        await using var db = NewDb(nameof(ResolveAsync_CabezaConDosHijos_DevuelveGroupConPadreYAmbosHijos));
        db.Tenants.AddRange(
            Row(Parent, isGroupParent: true),
            Row(Child1, parent: Parent),
            Row(Child2, parent: Parent),
            Row(Lonely));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeTrue();
        scope.IsAll.Should().BeFalse();
        scope.WriteTenantId.Should().Be(Parent);
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent, Child1, Child2]);
        scope.CanRead(Lonely).Should().BeFalse();
        scope.CanWrite(Child1).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_CabezaConHijoInactivo_IncluyeAlInactivo()
    {
        // Desvincular ≠ desactivar: el padre sigue viendo el histórico de un hijo suspendido.
        await using var db = NewDb(nameof(ResolveAsync_CabezaConHijoInactivo_IncluyeAlInactivo));
        db.Tenants.AddRange(
            Row(Parent, isGroupParent: true),
            Row(Child1, parent: Parent, isActive: false));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeTrue();
        scope.ReadTenantIds.Should().Contain(Child1);
    }

    [Fact]
    public async Task ResolveAsync_CabezaMarcadaSinHijos_DevuelveSingle()
    {
        await using var db = NewDb(nameof(ResolveAsync_CabezaMarcadaSinHijos_DevuelveSingle));
        db.Tenants.Add(Row(Parent, isGroupParent: true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent]);
    }

    // ── AC3 — fallo del resolver ⇒ Single, jamás All ───────────────────────────

    [Fact]
    public async Task ResolveAsync_TenantInexistente_DevuelveSingle()
    {
        await using var db = NewDb(nameof(ResolveAsync_TenantInexistente_DevuelveSingle));

        var scope = await Sut(db).ResolveAsync(Lonely, TestContext.Current.CancellationToken);

        scope.IsAll.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([Lonely]);
        scope.WriteTenantId.Should().Be(Lonely);
    }

    [Fact]
    public async Task ResolveAsync_DbContextLiberado_DevuelveSingle_NuncaAll()
    {
        var db = NewDb(nameof(ResolveAsync_DbContextLiberado_DevuelveSingle_NuncaAll));
        var sut = Sut(db);
        await db.DisposeAsync(); // la siguiente consulta lanza ObjectDisposedException

        var scope = await sut.ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsAll.Should().BeFalse("fail-closed: una excepción del resolver nunca abre el alcance");
        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent]);
    }

    [Fact]
    public async Task ResolveAsync_Cancelado_PropagaLaCancelacion()
    {
        await using var db = NewDb(nameof(ResolveAsync_Cancelado_PropagaLaCancelacion));
        db.Tenants.Add(Row(Parent, isGroupParent: true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Sut(db).ResolveAsync(Parent, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Contrato ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_GuidEmpty_LanzaArgumentException()
    {
        await using var db = NewDb(nameof(ResolveAsync_GuidEmpty_LanzaArgumentException));

        var act = () => Sut(db).ResolveAsync(Guid.Empty, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Ctor_DependenciasNulas_Lanzan()
    {
        using var db = NewDb(nameof(Ctor_DependenciasNulas_Lanzan));

        var sinDb = () => new DbTenantScopeResolver(null!, NullLogger<DbTenantScopeResolver>.Instance);
        var sinLogger = () => new DbTenantScopeResolver(db, null!);

        sinDb.Should().Throw<ArgumentNullException>();
        sinLogger.Should().Throw<ArgumentNullException>();
    }
}
