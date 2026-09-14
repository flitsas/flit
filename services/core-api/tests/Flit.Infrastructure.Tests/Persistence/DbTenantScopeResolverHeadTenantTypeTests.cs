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
/// HU #12406 AC4 — <see cref="DbTenantScopeResolver"/> deriva la clase de la cabeza de su
/// <c>tenant_type</c> (CONCESION | MARCA_BLANCA), leído de la MISMA fila que <c>is_group_parent</c>,
/// y la expone en <see cref="TenantScope.GroupKind"/>; nunca la recibe por parámetro. Complementa
/// <see cref="DbTenantScopeResolverTests"/> sin modificarlo.
/// <para>
/// Uso de ejemplo: <c>await new DbTenantScopeResolver(db, switches, logger).ResolveAsync(parent)</c>
/// devuelve <c>Group</c> con <c>GroupKind.MarcaBlanca</c> si la fila dice <c>tenant_type = MARCA_BLANCA</c>.
/// </para>
/// </summary>
public sealed class DbTenantScopeResolverHeadTenantTypeTests
{
    private static readonly Guid Parent = Guid.Parse("aaaaaaaa-0000-0000-0000-000000012406");
    private static readonly Guid Child = Guid.Parse("bbbbbbbb-0000-0000-0000-000000012406");

    private static FlitDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(name).Options);

    private static Tenant Row(Guid id, bool isGroupParent, string tenantType, Guid? parent = null) => new()
    {
        Id = id,
        Code = id.ToString("N")[..8],
        LegalName = "T " + id.ToString("N")[..8],
        TaxId = id.ToString("N")[..9],
        TenantType = tenantType,
        IsActive = true,
        IsGroupParent = isGroupParent,
        ParentTenantId = parent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static DbTenantScopeResolver Sut(FlitDbContext db) =>
        new(db, new AlwaysOnSwitches(), NullLogger<DbTenantScopeResolver>.Instance);

    private sealed class AlwaysOnSwitches : IHierarchySwitches
    {
        public Task<bool> IsGroupReadScopeEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> IsInheritedConfigurationEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<HierarchySwitchState>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HierarchySwitchState>>([]);

        public Task<HierarchySwitchState?> SetAsync(string key, bool isEnabled, Guid? actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<HierarchySwitchState?>(null);
    }

    [Theory]
    [InlineData("CONCESION", GroupKind.Concesion)]
    [InlineData("MARCA_BLANCA", GroupKind.MarcaBlanca)]
    public async Task ResolveAsync_CabezaConTipoDeCabeza_ExponeLaClaseJuntoAlConjuntoDeLectura(string tenantType, GroupKind esperada)
    {
        await using var db = NewDb(nameof(ResolveAsync_CabezaConTipoDeCabeza_ExponeLaClaseJuntoAlConjuntoDeLectura) + tenantType);
        db.Tenants.AddRange(Row(Parent, isGroupParent: true, tenantType), Row(Child, isGroupParent: false, "CONCESIONARIO", parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeTrue();
        scope.GroupKind.Should().Be(esperada);
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent, Child]);
        scope.WriteTenantId.Should().Be(Parent);
    }

    [Theory]
    [InlineData("CONCESIONARIO")]
    [InlineData("RENTING")]
    [InlineData("FLIT")]
    [InlineData("concesion")]
    [InlineData("")]
    public async Task ResolveAsync_MarcadaCabezaPeroSinTipoDeCabeza_DegradaASingleFailClosed(string tenantType)
    {
        // Imposible con ck_tenants_group_parent_by_type; solo por dato corrupto o migración a medias.
        // Una cabeza sin clase no actúa como grupo: no amplía la lectura a los hijos.
        await using var db = NewDb(nameof(ResolveAsync_MarcadaCabezaPeroSinTipoDeCabeza_DegradaASingleFailClosed) + tenantType);
        db.Tenants.AddRange(Row(Parent, isGroupParent: true, tenantType), Row(Child, isGroupParent: false, "CONCESIONARIO", parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeFalse();
        scope.GroupKind.Should().BeNull();
        scope.ReadTenantIds.Should().BeEquivalentTo([Parent]);
        scope.CanRead(Child).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_TipoDeCabezaSinMarcaDeCabeza_EsSingle()
    {
        // La otra cara del dato corrupto: el tipo dice cabeza pero is_group_parent no. Fail-closed.
        await using var db = NewDb(nameof(ResolveAsync_TipoDeCabezaSinMarcaDeCabeza_EsSingle));
        db.Tenants.AddRange(Row(Parent, isGroupParent: false, "MARCA_BLANCA"), Row(Child, isGroupParent: false, "CONCESIONARIO", parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scope = await Sut(db).ResolveAsync(Parent, TestContext.Current.CancellationToken);

        scope.IsGroup.Should().BeFalse();
        scope.GroupKind.Should().BeNull();
        scope.CanRead(Child).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_HijoYClienteAislado_NoExponenClase()
    {
        await using var db = NewDb(nameof(ResolveAsync_HijoYClienteAislado_NoExponenClase));
        db.Tenants.AddRange(Row(Parent, isGroupParent: true, "MARCA_BLANCA"), Row(Child, isGroupParent: false, "RENTING", parent: Parent));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var hijo = await Sut(db).ResolveAsync(Child, TestContext.Current.CancellationToken);
        var desconocido = await Sut(db).ResolveAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        hijo.GroupKind.Should().BeNull("la clase es de la cabeza, no del hijo");
        hijo.IsGroup.Should().BeFalse();
        desconocido.GroupKind.Should().BeNull();
        desconocido.IsGroup.Should().BeFalse();
    }
}
