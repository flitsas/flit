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
/// HU #12323 (Feature #12254) — <see cref="DbHierarchySwitches"/> sobre <c>identity.hierarchy_switches</c>
/// (InMemory). Uso de ejemplo:
/// <code>
/// var switches = new DbHierarchySwitches(db, NullLogger&lt;DbHierarchySwitches&gt;.Instance);
/// if (!await switches.IsGroupReadScopeEnabledAsync(ct)) return TenantScope.Single(tenantId);
/// </code>
/// </summary>
public sealed class DbHierarchySwitchesTests
{
    private static readonly Guid Actor = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    private static FlitDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(name).Options);

    private static HierarchySwitch Row(string key, bool isEnabled = true) => new()
    {
        Id = Guid.NewGuid(),
        SwitchKey = key,
        IsEnabled = isEnabled,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static async Task<FlitDbContext> SeededDb(string name, bool groupRead = true, bool inherited = true)
    {
        var db = NewDb(name);
        db.HierarchySwitches.AddRange(
            Row(HierarchySwitch.GroupReadScopeKey, groupRead),
            Row(HierarchySwitch.InheritedConfigurationKey, inherited));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static DbHierarchySwitches Sut(FlitDbContext db) =>
        new(db, NullLogger<DbHierarchySwitches>.Instance);

    // ── Contrato: las claves del dominio y de la entidad son las mismas ─────────

    [Fact]
    public void LasClavesDelDominioCoincidenConLasDeLaEntidad()
    {
        IHierarchySwitches.GroupReadScopeKey.Should().Be(HierarchySwitch.GroupReadScopeKey);
        IHierarchySwitches.InheritedConfigurationKey.Should().Be(HierarchySwitch.InheritedConfigurationKey);
    }

    // ── AC1 — lectura por petición ──────────────────────────────────────────────

    [Fact]
    public async Task IsGroupReadScopeEnabled_FilaEncendida_DevuelveTrue()
    {
        await using var db = await SeededDb(nameof(IsGroupReadScopeEnabled_FilaEncendida_DevuelveTrue));

        (await Sut(db).IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task IsGroupReadScopeEnabled_FilaApagada_DevuelveFalse()
    {
        await using var db = await SeededDb(nameof(IsGroupReadScopeEnabled_FilaApagada_DevuelveFalse), groupRead: false);

        (await Sut(db).IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task IsGroupReadScopeEnabled_SinCache_ReflejaElCambioEnLaSiguienteLlamada()
    {
        // AC4: apagar y reencender sin recrear el servicio ni el contexto.
        await using var db = await SeededDb(nameof(IsGroupReadScopeEnabled_SinCache_ReflejaElCambioEnLaSiguienteLlamada));
        var sut = Sut(db);

        (await sut.IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        await sut.SetAsync(HierarchySwitch.GroupReadScopeKey, false, Actor, TestContext.Current.CancellationToken);
        (await sut.IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        await sut.SetAsync(HierarchySwitch.GroupReadScopeKey, true, Actor, TestContext.Current.CancellationToken);
        (await sut.IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    // ── Fail-closed ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsGroupReadScopeEnabled_SinFila_DevuelveFalse()
    {
        await using var db = NewDb(nameof(IsGroupReadScopeEnabled_SinFila_DevuelveFalse));

        (await Sut(db).IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeFalse(
            "fail-closed: sin fila el interruptor se asume apagado");
    }

    [Fact]
    public async Task IsInheritedConfigurationEnabled_SinFila_DevuelveFalse()
    {
        await using var db = NewDb(nameof(IsInheritedConfigurationEnabled_SinFila_DevuelveFalse));

        (await Sut(db).IsInheritedConfigurationEnabledAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task IsGroupReadScopeEnabled_DbContextLiberado_DevuelveFalse_NoLanza()
    {
        var db = NewDb(nameof(IsGroupReadScopeEnabled_DbContextLiberado_DevuelveFalse_NoLanza));
        var sut = Sut(db);
        await db.DisposeAsync();

        var enabled = await sut.IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken);

        enabled.Should().BeFalse("una excepción de lectura nunca enciende el interruptor");
    }

    [Fact]
    public async Task IsGroupReadScopeEnabled_Cancelado_PropagaLaCancelacion()
    {
        await using var db = await SeededDb(nameof(IsGroupReadScopeEnabled_Cancelado_PropagaLaCancelacion));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Sut(db).IsGroupReadScopeEnabledAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── AC2 — interruptores independientes ──────────────────────────────────────

    [Fact]
    public async Task SetAsync_ApagarGroupReadScope_NoCambiaInheritedConfiguration()
    {
        await using var db = await SeededDb(nameof(SetAsync_ApagarGroupReadScope_NoCambiaInheritedConfiguration));
        var sut = Sut(db);

        await sut.SetAsync(HierarchySwitch.GroupReadScopeKey, false, Actor, TestContext.Current.CancellationToken);

        (await sut.IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await sut.IsInheritedConfigurationEnabledAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_ApagarInheritedConfiguration_NoCambiaGroupReadScope()
    {
        await using var db = await SeededDb(nameof(SetAsync_ApagarInheritedConfiguration_NoCambiaGroupReadScope));
        var sut = Sut(db);

        await sut.SetAsync(HierarchySwitch.InheritedConfigurationKey, false, Actor, TestContext.Current.CancellationToken);

        (await sut.IsInheritedConfigurationEnabledAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await sut.IsGroupReadScopeEnabledAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_NoTocaLaJerarquiaDeTenants()
    {
        // AC1/AC2: el interruptor no es is_group_parent ni parent_tenant_id.
        await using var db = await SeededDb(nameof(SetAsync_NoTocaLaJerarquiaDeTenants));
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        db.Tenants.AddRange(
            new Tenant { Id = parent, Code = "P1", LegalName = "P", TaxId = "1", TenantType = "CONCESIONARIO", IsActive = true, IsGroupParent = true, CreatedAt = DateTimeOffset.UtcNow },
            new Tenant { Id = child, Code = "C1", LegalName = "C", TaxId = "2", TenantType = "CONCESIONARIO", IsActive = true, ParentTenantId = parent, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Sut(db).SetAsync(HierarchySwitch.GroupReadScopeKey, false, Actor, TestContext.Current.CancellationToken);

        var p = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == parent, TestContext.Current.CancellationToken);
        var c = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == child, TestContext.Current.CancellationToken);
        p.IsGroupParent.Should().BeTrue();
        c.ParentTenantId.Should().Be(parent);
    }

    // ── SetAsync / ListAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_ClaveValida_ActualizaEstadoFechaYActor()
    {
        await using var db = await SeededDb(nameof(SetAsync_ClaveValida_ActualizaEstadoFechaYActor));
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var state = await Sut(db).SetAsync(HierarchySwitch.GroupReadScopeKey, false, Actor, TestContext.Current.CancellationToken);

        state.Should().NotBeNull();
        state!.Key.Should().Be(HierarchySwitch.GroupReadScopeKey);
        state.IsEnabled.Should().BeFalse();
        state.UpdatedBy.Should().Be(Actor);
        state.UpdatedAt.Should().BeAfter(before);

        var row = await db.HierarchySwitches.AsNoTracking()
            .SingleAsync(s => s.SwitchKey == HierarchySwitch.GroupReadScopeKey, TestContext.Current.CancellationToken);
        row.IsEnabled.Should().BeFalse();
        row.UpdatedBy.Should().Be(Actor);
    }

    [Theory]
    [InlineData("unknown_switch")]
    [InlineData("GROUP_READ_SCOPE")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetAsync_ClaveDesconocida_DevuelveNullYNoCreaFilas(string key)
    {
        await using var db = await SeededDb(nameof(SetAsync_ClaveDesconocida_DevuelveNullYNoCreaFilas) + key.Length);

        var state = await Sut(db).SetAsync(key, false, Actor, TestContext.Current.CancellationToken);

        state.Should().BeNull();
        (await db.HierarchySwitches.CountAsync(TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Fact]
    public async Task SetAsync_ClaveConocidaSinFila_DevuelveNull()
    {
        await using var db = NewDb(nameof(SetAsync_ClaveConocidaSinFila_DevuelveNull));

        var state = await Sut(db).SetAsync(HierarchySwitch.GroupReadScopeKey, true, Actor, TestContext.Current.CancellationToken);

        state.Should().BeNull("SetAsync no crea filas: el seed es responsabilidad de la migración");
    }

    [Fact]
    public async Task ListAsync_DevuelveLosDosInterruptoresOrdenadosPorClave()
    {
        await using var db = await SeededDb(nameof(ListAsync_DevuelveLosDosInterruptoresOrdenadosPorClave), groupRead: false);

        var items = await Sut(db).ListAsync(TestContext.Current.CancellationToken);

        items.Select(i => i.Key).Should().ContainInOrder(
            HierarchySwitch.GroupReadScopeKey, HierarchySwitch.InheritedConfigurationKey);
        items.Single(i => i.Key == HierarchySwitch.GroupReadScopeKey).IsEnabled.Should().BeFalse();
        items.Single(i => i.Key == HierarchySwitch.InheritedConfigurationKey).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Ctor_DependenciasNulas_Lanzan()
    {
        using var db = NewDb(nameof(Ctor_DependenciasNulas_Lanzan));

        var sinDb = () => new DbHierarchySwitches(null!, NullLogger<DbHierarchySwitches>.Instance);
        var sinLogger = () => new DbHierarchySwitches(db, null!);

        sinDb.Should().Throw<ArgumentNullException>();
        sinLogger.Should().Throw<ArgumentNullException>();
    }
}
