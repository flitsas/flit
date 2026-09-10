using System.Linq;
using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12238 (Feature #12236) — valida el DDL embebido de <c>admin.banners</c>
/// (107-HU12238-admin-banners.sql) y la migración <c>HU12238_AdminBanners</c> contra
/// los AC de la historia.
///
/// Uso de ejemplo:
/// var sql = EmbeddedDdl.LoadUp("107-HU12238-admin-banners.sql");
/// sql.Should().Contain("admin.banners");
/// </summary>
public sealed class Hu12238AdminBannersSchemaTests
{
    private const string DdlFileName = "107-HU12238-admin-banners.sql";
    private const string MigrationTypeName = "Flit.Infrastructure.Migrations.HU12238_AdminBanners";

    // ---- AC1 — Migración crea la tabla correctamente ----

    [Fact]
    public void Ddl_107_AdminBanners_HasRequiredObjects()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        sql.Should().Contain("CREATE TABLE admin.banners");
        sql.Should().Contain("name varchar(200) NOT NULL");
        sql.Should().Contain("image_storage_path varchar(200) NOT NULL");
        sql.Should().Contain("image_sha256 char(64) NOT NULL");
        sql.Should().Contain("link_url varchar(2048)");
        sql.Should().Contain("is_active boolean NOT NULL DEFAULT true");
        sql.Should().Contain("row_version bigint NOT NULL DEFAULT 0");
        sql.Should().Contain("ix_banners_activo_vigencia");
        sql.Should().Contain("tr_banners_row_version");
        sql.Should().Contain("tr_banners_audit");
        sql.Should().Contain("ADR-0058-banners-tabla-global-sin-tenant-excepcion");
    }

    [Fact]
    public void Ddl_107_AdminBanners_SinTenantIdNiRls_ExcepcionDocumentada()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        // No columna tenant_id real (la palabra aparece solo en el COMMENT explicando
        // la excepcion de ADR-0058, por eso se valida la ausencia de la DECLARACION,
        // no la palabra aislada).
        sql.Should().NotContain("tenant_id uuid");
        sql.Should().NotContain("REFERENCES identity.tenants");
        sql.Should().NotContain("ENABLE ROW LEVEL SECURITY");
        sql.Should().NotContain("tenant_isolation");
        sql.Should().Contain("Sin RLS: la tabla es global por diseño (ADR-0058)");
    }

    [Fact]
    public void Ddl_107_AdminBanners_TieneColumnasEstandarDeAuditoria()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        sql.Should().Contain("created_at timestamptz NOT NULL DEFAULT now()");
        sql.Should().Contain("created_by uuid");
        sql.Should().Contain("updated_at timestamptz");
        sql.Should().Contain("updated_by uuid");
        sql.Should().Contain("deleted_at timestamptz");
        sql.Should().Contain("deleted_by uuid");
    }

    // ---- AC2 — Vigencia opcional (banner sin fecha programada) ----

    [Fact]
    public void Ddl_107_AdminBanners_CheckConstraint_PermiteAmbasFechasNulas()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        sql.Should().Contain("CONSTRAINT ck_banners_vigencia CHECK");
        sql.Should().Contain("(valid_from IS NULL AND valid_until IS NULL)");
    }

    [Fact]
    public void Ddl_107_AdminBanners_ValidFromValidUntil_SonNullable()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        // A diferencia del DDL de referencia de ADR-0058 (NOT NULL), estas columnas
        // deben declararse SIN "NOT NULL" para permitir banners sin vigencia programada.
        sql.Should().Contain("valid_from timestamptz,");
        sql.Should().Contain("valid_until timestamptz,");
        sql.Should().NotContain("valid_from timestamptz NOT NULL");
        sql.Should().NotContain("valid_until timestamptz NOT NULL");
    }

    [Fact]
    public void Ddl_107_AdminBanners_CheckConstraint_MantieneOrdenCuandoAmbasPresentes()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        sql.Should().Contain("valid_until > valid_from");
    }

    // ---- AC3 — Migración no aplicada por archivo faltante ----

    [Fact]
    public void Migration_HU12238_AdminBanners_TieneAtributoMigrationParaSerDescubierta()
    {
        var migrationType = typeof(EmbeddedDdl).Assembly.GetType(MigrationTypeName, throwOnError: true)!;

        var migrationAttr = migrationType.GetCustomAttribute<MigrationAttribute>();

        migrationAttr.Should().NotBeNull(
            "sin [Migration] el runtime de EF no descubre la migración y admin.banners no se crea (AC3)");
        migrationAttr!.Id.Should().Be("20260909180000_HU12238_AdminBanners");
    }

    [Fact]
    public void Migration_HU12238_AdminBanners_ApuntaAlFlitDbContext()
    {
        var migrationType = typeof(EmbeddedDdl).Assembly.GetType(MigrationTypeName, throwOnError: true)!;

        var dbContextAttr = migrationType.GetCustomAttribute<DbContextAttribute>();

        dbContextAttr.Should().NotBeNull(
            "sin [DbContext(typeof(FlitDbContext))] la migración no se asocia al contexto correcto (AC3)");
        dbContextAttr!.ContextType.Should().Be<FlitDbContext>();
    }

    [Fact]
    public void Migration_HU12238_AdminBanners_EsUnaMigracionEfValida()
    {
        var migrationType = typeof(EmbeddedDdl).Assembly.GetType(MigrationTypeName, throwOnError: true)!;

        typeof(Migration).IsAssignableFrom(migrationType).Should().BeTrue();
        migrationType.GetMethod("Up", BindingFlags.NonPublic | BindingFlags.Instance).Should().NotBeNull();
        migrationType.GetMethod("Down", BindingFlags.NonPublic | BindingFlags.Instance).Should().NotBeNull();
    }
}
