using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Integration.Tests.Harness;

/// <summary>
/// HU #12319 (AC4) — SQL directo verificable contra el motor real: una consulta cross-schema
/// representativa del producto (<c>identity.tenants ⨝ admin.tenant_transit_office_grants</c>,
/// filtrada con <c>= ANY(@ids)</c> como hacen los repositorios de lectura) sobre TRES tenants, por
/// las dos vías que usa el código: <c>Database.SqlQueryRaw&lt;T&gt;</c> de EF y <c>NpgsqlCommand</c>.
/// <para>
/// Uso de ejemplo: sembrar catálogo de OT + tenants + grants con el <c>FlitDbContext</c> real y
/// afirmar filas por tenant. Es la consulta base de la lectura ampliada de la cabeza de grupo
/// (Feature #12254): «qué OT puede ver cada cliente del grupo».
/// </para>
/// </summary>
public sealed class RawSqlCrossSchemaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid OtBogota = new("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid OtMedellin = new("aaaaaaaa-0001-4000-8000-000000000002");
    private static readonly Guid OtCali = new("aaaaaaaa-0001-4000-8000-000000000003");

    private const string GrantsByTenantSql =
        """
        SELECT t.id AS tenant_id,
               t.code AS tenant_code,
               count(g.id) FILTER (WHERE g.is_enabled)::int AS enabled_grants,
               count(g.id)::int AS total_grants
          FROM identity.tenants t
          LEFT JOIN admin.tenant_transit_office_grants g ON g.tenant_id = t.id
         WHERE t.id = ANY(@ids)
         GROUP BY t.id, t.code
         ORDER BY t.code
        """;

    [PostgresFact]
    public async Task SqlQueryRaw_de_EF_cuenta_grants_por_tenant_con_ANY_de_ids()
    {
        await SeedThreeTenantsAsync();
        Guid[] ids = [TenantSeed.ParentId, TenantSeed.ChildId, TenantSeed.LoneId];

        await using var ctx = NewContext();
        var rows = await ctx.Database
            .SqlQueryRaw<GrantsByTenantRow>(GrantsByTenantSql, new NpgsqlParameter("ids", ids))
            .ToListAsync();

        rows.Should().HaveCount(3);
        rows.Select(r => r.TenantCode).Should().Equal("IT-CHILD", "IT-LONE", "IT-PARENT");
        rows.Single(r => r.TenantId == TenantSeed.ParentId).Should().BeEquivalentTo(new { EnabledGrants = 2, TotalGrants = 2 });
        rows.Single(r => r.TenantId == TenantSeed.ChildId).Should().BeEquivalentTo(new { EnabledGrants = 1, TotalGrants = 2 });
        rows.Single(r => r.TenantId == TenantSeed.LoneId).Should().BeEquivalentTo(new { EnabledGrants = 0, TotalGrants = 0 });
    }

    [PostgresFact]
    public async Task ANY_de_ids_filtra_de_verdad_solo_los_tenants_pedidos()
    {
        await SeedThreeTenantsAsync();
        Guid[] ids = [TenantSeed.ChildId];

        await using var ctx = NewContext();
        var rows = await ctx.Database
            .SqlQueryRaw<GrantsByTenantRow>(GrantsByTenantSql, new NpgsqlParameter("ids", ids))
            .ToListAsync();

        rows.Should().ContainSingle().Which.TenantId.Should().Be(TenantSeed.ChildId);
    }

    [PostgresFact]
    public async Task NpgsqlCommand_directo_devuelve_las_mismas_filas_que_EF()
    {
        await SeedThreeTenantsAsync();
        Guid[] ids = [TenantSeed.ParentId, TenantSeed.ChildId, TenantSeed.LoneId];

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(GrantsByTenantSql, connection);
        cmd.Parameters.AddWithValue("ids", ids);

        var rows = new List<(Guid TenantId, string Code, int Enabled, int Total)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
            }
        }

        rows.Should().Equal(
            (TenantSeed.ChildId, "IT-CHILD", 1, 2),
            (TenantSeed.LoneId, "IT-LONE", 0, 0),
            (TenantSeed.ParentId, "IT-PARENT", 2, 2));
    }

    /// <summary>El JOIN con el catálogo también es real: la FK a <c>catalogs.transit_offices</c> rechaza una OT inexistente.</summary>
    [PostgresFact]
    public async Task Grant_a_una_OT_inexistente_es_rechazado_por_la_FK_al_catalogo()
    {
        await SeedThreeTenantsAsync();

        await using var ctx = NewContext();
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = TenantSeed.LoneId,
            TransitOfficeId = Guid.NewGuid(),
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var caught = await ctx.Invoking(c => c.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        var pg = caught.Which.InnerException.Should().BeOfType<PostgresException>().Which;
        pg.SqlState.Should().Be("23503");
        pg.ConstraintName.Should().Be("fk_tenant_transit_office_grants_transit_offices");
    }

    // ── datos: 3 OT de catálogo, 3 tenants, 4 grants (parent 2 activos, child 1 activo + 1 inactivo, lone 0) ──

    private async Task SeedThreeTenantsAsync()
    {
        await using var ctx = NewContext();

        ctx.TransitOffices.AddRange(
            NewOffice(OtBogota, "11001000", "BOGOTA", "11", "11001"),
            NewOffice(OtMedellin, "5001000", "MEDELLIN", "05", "05001"),
            NewOffice(OtCali, "76001000", "CALI", "76", "76001"));

        ctx.Tenants.Add(TenantSeed.GroupParent());
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();

        // El hijo va aparte: el trigger BEFORE INSERT necesita que el padre ya esté en la tabla y EF
        // no garantiza el orden de los INSERT de un mismo SaveChanges entre filas sin FK navegable.
        ctx.Tenants.Add(TenantSeed.ChildOf(TenantSeed.ParentId));
        await ctx.SaveChangesAsync();

        ctx.TenantTransitOfficeGrants.AddRange(
            NewGrant(TenantSeed.ParentId, OtBogota, enabled: true),
            NewGrant(TenantSeed.ParentId, OtMedellin, enabled: true),
            NewGrant(TenantSeed.ChildId, OtBogota, enabled: true),
            NewGrant(TenantSeed.ChildId, OtCali, enabled: false));
        await ctx.SaveChangesAsync();
    }

    private static TransitOffice NewOffice(Guid id, string code, string name, string department, string city) => new()
    {
        Id = id,
        Code = code,
        Name = $"SECRETARIA DE MOVILIDAD DE {name}",
        DepartmentCode = department,
        CityCode = city,
        IsActive = true,
    };

    private static TenantTransitOfficeGrant NewGrant(Guid tenantId, Guid officeId, bool enabled) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        TransitOfficeId = officeId,
        IsEnabled = enabled,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Fila sin mapear del <c>SqlQueryRaw</c>: EF la hidrata por nombre de columna, en snake_case por la convención del contexto (<c>tenant_id</c> → <c>TenantId</c>).</summary>
    private sealed class GrantsByTenantRow
    {
        public Guid TenantId { get; set; }

        public string TenantCode { get; set; } = string.Empty;

        public int EnabledGrants { get; set; }

        public int TotalGrants { get; set; }
    }
}
