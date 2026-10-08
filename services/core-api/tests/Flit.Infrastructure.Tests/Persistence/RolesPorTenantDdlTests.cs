using System.Reflection;
using System.Text;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13440 — aislamiento por tenant en <c>security.roles</c>. Se verifica el texto del DDL 134 (embebido), el
/// modelo EF y el descubrimiento de la migración. El comportamiento contra el motor (unicidad, trigger, RLS) se
/// validó con una base de scratch y queda documentado en las evidencias de la HU.
/// </summary>
public sealed class RolesPorTenantDdlTests
{
    private const string Resource = "Flit.Infrastructure.Persistence.Sql.Ddl.134-HU13440-roles-por-tenant.sql";

    private static string LoadStatements()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(Resource);
        stream.Should().NotBeNull($"el DDL embebido {Resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(l =>
        {
            var corte = l.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? l[..corte] : l;
        }));
    }

    [Fact]
    public void AC1_AgregaTenantIdNullableConFkATenantsSinTocarLasFilasExistentes()
    {
        var ddl = LoadStatements();

        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS tenant_id uuid;");
        ddl.Should().NotContain("tenant_id uuid NOT NULL");
        ddl.Should().Contain("FOREIGN KEY (tenant_id)").And.Contain("REFERENCES identity.tenants(id)");
        ddl.Should().NotContain("UPDATE security.roles", "los roles actuales conservan tenant_id NULL");
    }

    [Fact]
    public void AC2_UnicidadGlobalPorCodeYTipoUnicidadPorTenantYTriggerContraCodesGlobales()
    {
        var ddl = LoadStatements();

        ddl.Should().Contain("DROP INDEX IF EXISTS security.uq_roles_code_target_entity_type;");
        ddl.Should().Contain("ON security.roles (code, target_entity_type)")
            .And.Contain("WHERE deleted_at IS NULL AND tenant_id IS NULL");
        ddl.Should().Contain("uq_roles_tenant_code").And.Contain("ON security.roles (tenant_id, code)")
            .And.Contain("WHERE deleted_at IS NULL AND tenant_id IS NOT NULL");
        ddl.Should().Contain("tr_roles_tenant_code_not_global")
            .And.Contain("g.tenant_id IS NULL AND g.deleted_at IS NULL AND g.code = NEW.code")
            .And.Contain("ERRCODE = '23505'");
    }

    [Fact]
    public void AC3_AC4_LaPolicyDeRlsVeGlobalesPropiosYElSuperAdminVeTodo()
    {
        var ddl = LoadStatements();

        ddl.Should().Contain("ENABLE ROW LEVEL SECURITY");
        ddl.Should().NotContain("FORCE ROW LEVEL SECURITY", "la app conecta como owner; el patrón del repo es RLS nominal");
        ddl.Should().Contain("tenant_id IS NULL")
            .And.Contain("app.is_superadmin")
            .And.Contain("app.current_tenant_id");
    }

    [Fact]
    public void ElDdlEsIdempotente()
    {
        var ddl = LoadStatements();

        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS")
            .And.Contain("CREATE INDEX IF NOT EXISTS")
            .And.Contain("CREATE UNIQUE INDEX IF NOT EXISTS")
            .And.Contain("DROP TRIGGER IF EXISTS")
            .And.Contain("DROP POLICY IF EXISTS")
            .And.Contain("CREATE OR REPLACE FUNCTION");
    }

    [Fact]
    public void ElModeloEfMapeaTenantIdNullableComoTenant_idYLaMigracionSeDescubre()
    {
        using var db = new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(nameof(RolesPorTenantDdlTests)).Options);
        var prop = db.Model.FindEntityType(typeof(Role))!.FindProperty(nameof(Role.TenantId));

        prop.Should().NotBeNull();
        prop!.IsNullable.Should().BeTrue();
        prop.GetColumnName().Should().Be("tenant_id");

        var migracion = typeof(HU13440_RolesPorTenant);
        migracion.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<FlitDbContext>();
        migracion.GetCustomAttribute<MigrationAttribute>()!.Id.Should().Be("20261008140000_HU13440_RolesPorTenant");
    }

    [Fact]
    public void UnRolNuevoEsGlobalPorDefecto()
    {
        new Role().TenantId.Should().BeNull();
    }
}
