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
/// var resolver = new DbTenantScopeResolver(db, switches, NullLogger&lt;DbTenantScopeResolver&gt;.Instance);
/// var scope = await resolver.ResolveAsync(tenantId, ct); // Single o Group, nunca All
/// </code>
/// HU #12323: el resolver consulta primero <see cref="IHierarchySwitches"/> (fake aquí, encendido por
/// defecto); apagado ⇒ <c>Single</c> sin tocar la jerarquía y reencendido ⇒ <c>Group</c> en la siguiente
/// llamada, sin recrear nada (lectura por petición, sin caché).
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

    private static DbTenantScopeResolver Sut(FlitDbContext db, FakeHierarchySwitches? switches = null) =>
        new(db, switches ?? new FakeHierarchySwitches(), NullLogger<DbTenantScopeResolver>.Instance);

    /// <summary>Doble de <see cref="IHierarchySwitches"/>: encendido por defecto, conmutable entre llamadas.</summary>
    private sealed class FakeHierarchySwitches : IHierarchySwitches
    {
        public bool GroupReadScope { get; set; } = true;

        public bool InheritedConfiguration { get; set; } = true;

        public int GroupReadScopeReads { get; private set; }

        public Task<bool> IsGroupReadScopeEnabledAsync(CancellationToken cancellationToken = default)
        {
            GroupReadScopeReads++;
            return Task.FromResult(GroupReadScope);
        }

        public Task<bool> IsInheritedConfigurationEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InheritedConfiguration);

        public Task<IReadOnlyList<HierarchySwitchState>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HierarchySwitchState>>([]);

        public Task<HierarchySwitchState?> SetAsync(string key, bool isEnabled, Guid? actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<HierarchySwitchState?>(null);
    }

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

    // ── HU #12323 AC1 — interruptor group_read_scope apagado ⇒ Single sin tocar datos ─

    [Fact]
    public async Task ResolveAsync_InterruptorApagado_CabezaConHijos_DevuelveSingle()
    {
        await using var db = NewDb(nameof(ResolveAsync_InterruptorApagado_CabezaConHijos_DevuelveSingle));
        db.Tenants.AddRange(
            Row(Parent, isGroupParent: true),
            Row(Child1, parent: Parent),
            Row(Child2, parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var switches = new FakeHierarchySwitches { GroupReadScope = false };

        var scope = await Sut(db, switches).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeFalse("con el interruptor apagado la cabeza resuelve alcance propio");
        scope.IsAll.Should().BeFalse();
        scope.WriteTenantId.Should().Be(Parent);
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent]);
        scope.CanRead(Child1).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_InterruptorApagado_DejaLaJerarquiaIntacta()
    {
        await using var db = NewDb(nameof(ResolveAsync_InterruptorApagado_DejaLaJerarquiaIntacta));
        db.Tenants.AddRange(Row(Parent, isGroupParent: true), Row(Child1, parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Sut(db, new FakeHierarchySwitches { GroupReadScope = false })
            .ResolveAsync(Parent, TestContext.Current.CancellationToken);

        var parent = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == Parent, TestContext.Current.CancellationToken);
        var child = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == Child1, TestContext.Current.CancellationToken);
        parent.IsGroupParent.Should().BeTrue("apagar el interruptor no toca is_group_parent");
        child.ParentTenantId.Should().Be(Parent, "apagar el interruptor no toca parent_tenant_id");
    }

    [Fact]
    public async Task ResolveAsync_InterruptorApagado_NoConsultaLaJerarquia_NiFallaSinContexto()
    {
        // Con el interruptor apagado el resolver no toca la BD: incluso con el contexto liberado
        // devuelve Single sin pasar por el catch (fail-closed por diseño, no por excepción).
        var db = NewDb(nameof(ResolveAsync_InterruptorApagado_NoConsultaLaJerarquia_NiFallaSinContexto));
        var sut = Sut(db, new FakeHierarchySwitches { GroupReadScope = false });
        await db.DisposeAsync();

        var scope = await sut.ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent]);
    }

    // ── HU #12323 AC2 — apagar inherited_configuration no cambia el alcance ────

    [Fact]
    public async Task ResolveAsync_SoloInheritedConfigurationApagado_SigueDevolviendoGroup()
    {
        await using var db = NewDb(nameof(ResolveAsync_SoloInheritedConfigurationApagado_SigueDevolviendoGroup));
        db.Tenants.AddRange(Row(Parent, isGroupParent: true), Row(Child1, parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var switches = new FakeHierarchySwitches { GroupReadScope = true, InheritedConfiguration = false };

        var scope = await Sut(db, switches).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeTrue("los interruptores son independientes: inherited_configuration no gobierna el alcance");
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent, Child1]);
    }

    // ── HU #12323 AC4 — reactivación ⇒ Group en la siguiente petición, sin caché ─

    [Fact]
    public async Task ResolveAsync_ApagarYReencender_VuelveAGroupEnLaSiguienteLlamada_SinCache()
    {
        await using var db = NewDb(nameof(ResolveAsync_ApagarYReencender_VuelveAGroupEnLaSiguienteLlamada_SinCache));
        db.Tenants.AddRange(Row(Parent, isGroupParent: true), Row(Child1, parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var switches = new FakeHierarchySwitches { GroupReadScope = true };
        var sut = Sut(db, switches);

        (await sut.ResolveAsync(Parent, TestContext.Current.CancellationToken)).IsGroup.Should().BeTrue();

        switches.GroupReadScope = false;
        (await sut.ResolveAsync(Parent, TestContext.Current.CancellationToken)).IsGroup.Should().BeFalse();

        switches.GroupReadScope = true;
        var reactivated = await sut.ResolveAsync(Parent, TestContext.Current.CancellationToken);

        reactivated.IsGroup.Should().BeTrue("la siguiente petición vuelve a Group sin recrear el resolver ni el token");
        reactivated.ReadTenantIds.Should().BeEquivalentTo([Parent, Child1]);
        switches.GroupReadScopeReads.Should().Be(3, "el interruptor se lee en CADA petición: no hay caché");
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

        var sinDb = () => new DbTenantScopeResolver(null!, new FakeHierarchySwitches(), NullLogger<DbTenantScopeResolver>.Instance);
        var sinSwitches = () => new DbTenantScopeResolver(db, null!, NullLogger<DbTenantScopeResolver>.Instance);
        var sinLogger = () => new DbTenantScopeResolver(db, new FakeHierarchySwitches(), null!);

        sinDb.Should().Throw<ArgumentNullException>();
        sinSwitches.Should().Throw<ArgumentNullException>();
        sinLogger.Should().Throw<ArgumentNullException>();
    }
}
