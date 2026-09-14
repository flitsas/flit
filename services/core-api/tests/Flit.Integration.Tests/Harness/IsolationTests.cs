using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Integration.Tests.Harness;

/// <summary>
/// HU #12319 (AC2) — aislamiento entre pruebas. <c>Siembra_A</c> y <c>Siembra_B</c> insertan el
/// MISMO tenant (mismo <c>id</c> y mismo <c>code</c>, ambos UNIQUE): si el reset entre pruebas no
/// funcionara, la segunda en ejecutarse fallaría con 23505. xUnit no garantiza el orden, y eso es
/// justo lo que se quiere demostrar: pasan en cualquier orden.
/// <para>
/// Uso de ejemplo: heredar de <see cref="PostgresTestBase"/> basta para arrancar cada prueba con la
/// base recién reseteada; <see cref="PostgresDatabaseFixture.ResetAsync"/> también puede llamarse
/// a mano dentro de una prueba.
/// </para>
/// </summary>
public sealed class IsolationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task Siembra_A_inserta_el_tenant_con_id_y_code_fijos()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.GroupParent());
        await ctx.SaveChangesAsync();

        (await ctx.Tenants.CountAsync(t => t.Id == TenantSeed.ParentId)).Should().Be(1);
    }

    [PostgresFact]
    public async Task Siembra_B_inserta_el_mismo_tenant_con_las_mismas_llaves()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.GroupParent());
        await ctx.SaveChangesAsync();

        (await ctx.Tenants.CountAsync(t => t.Id == TenantSeed.ParentId)).Should().Be(1);
    }

    [PostgresFact]
    public async Task Cada_prueba_arranca_sin_tenants_ni_bitacora_de_otra_prueba()
    {
        await using var ctx = NewContext();

        (await ctx.Tenants.AnyAsync()).Should().BeFalse("el reset trunca identity.tenants, incluidas las compañías mock de SeedMockCompanies");
        (await ctx.TenantHierarchyAuditEntries.AnyAsync()).Should().BeFalse();
    }

    [PostgresFact]
    public async Task Reset_vacia_las_tablas_de_trabajo_y_conserva_los_catalogos()
    {
        await using (var seed = NewContext())
        {
            seed.Tenants.Add(TenantSeed.GroupParent());
            seed.Tenants.Add(TenantSeed.ChildOf(TenantSeed.ParentId));
            await seed.SaveChangesAsync();
        }

        var catalogCountsBefore = await CountPreservedAsync();
        var resetsBefore = Fixture.ResetCount;

        await Fixture.ResetAsync();

        await using var ctx = NewContext();
        (await ctx.Tenants.AnyAsync()).Should().BeFalse();
        (await ctx.TenantHierarchyAuditEntries.AnyAsync()).Should().BeFalse();
        (await CountPreservedAsync()).Should().Equal(catalogCountsBefore, "las tablas de la lista blanca no se truncan");
        Fixture.ResetCount.Should().Be(resetsBefore + 1);
    }

    /// <summary>
    /// Los interruptores (HU #12323) están en la lista blanca (no se truncan), así que el reset los
    /// devuelve explícitamente a su seed (<c>is_enabled = true</c>): una prueba que apague uno no
    /// contamina a la siguiente.
    /// </summary>
    [PostgresFact]
    public async Task Reset_devuelve_los_interruptores_de_jerarquia_a_encendidos()
    {
        await using (var toggle = NewContext())
        {
            var sw = await toggle.HierarchySwitches.SingleAsync(s => s.SwitchKey == HierarchySwitch.GroupReadScopeKey);
            sw.IsEnabled = false;
            await toggle.SaveChangesAsync();
        }

        await Fixture.ResetAsync();

        await using var ctx = NewContext();
        var switches = await ctx.HierarchySwitches.AsNoTracking().ToListAsync();
        switches.Select(s => s.SwitchKey).Should().BeEquivalentTo(
            [HierarchySwitch.GroupReadScopeKey, HierarchySwitch.InheritedConfigurationKey]);
        switches.Should().AllSatisfy(s => s.IsEnabled.Should().BeTrue());
    }

    private async Task<List<long>> CountPreservedAsync()
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        var counts = new List<long>();
        foreach (var table in PostgresDatabaseFixture.PreservedSeededTables.Order(StringComparer.Ordinal))
        {
            var dot = table.IndexOf('.', StringComparison.Ordinal);
            await using var cmd = new Npgsql.NpgsqlCommand(
                $"SELECT count(*) FROM \"{table[..dot]}\".\"{table[(dot + 1)..]}\"", connection);
            counts.Add((long)(await cmd.ExecuteScalarAsync())!);
        }

        return counts;
    }
}
