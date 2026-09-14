using System.Text.RegularExpressions;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12361 (Feature #12257) — esquema de <c>tramites.network_access_audit</c> (DDL 113-, migración
/// <c>20260914120000_HU12361_NetworkAccessAudit</c>): sin Postgres, verifica que el DDL declare lo que
/// exige la HU (append-only, sin FK, RLS por hijo/alcanzado/actor sin FORCE, índices, CHECKs, comentarios
/// en español), que el <c>Down</c> lo revierta, que la entidad EF esté mapeada como el DDL y que el
/// snapshot no tenga drift sobre la tabla. Que las filas sobrevivan al desvínculo y el trigger rechace
/// UPDATE/DELETE se prueba contra Postgres real en <c>NetworkAccessAuditTests</c>.
/// Uso de ejemplo: no se invoca; corre en CI.
/// </summary>
public sealed class NetworkAccessAuditSchemaTests
{
    private const string DdlFile = "113-HU12361-network-access-audit.sql";

    private static string LoadUp() => EmbeddedDdl.LoadUp(DdlFile);

    private static string MigrationUpSql() =>
        new HU12361_NetworkAccessAudit().UpOperations.OfType<SqlOperation>().Single().Sql;

    private static string MigrationDownSql() =>
        new HU12361_NetworkAccessAudit().DownOperations.OfType<SqlOperation>().Single().Sql;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    private static IModel DesignModel(FlitDbContext db) => db.GetService<IDesignTimeModel>().Model;

    private static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ");

    [Fact]
    public void La_migracion_carga_el_DDL_embebido_113()
    {
        var up = MigrationUpSql();
        up.Should().Be(LoadUp());
        up.Should().Contain("CREATE TABLE IF NOT EXISTS tramites.network_access_audit");
    }

    [Fact]
    public void El_DDL_declara_append_only_sin_FK_con_RLS_e_indices()
    {
        var sql = Normalize(LoadUp());

        // Columnas exigidas.
        foreach (var col in new[] { "occurred_at timestamptz NOT NULL", "actor_user_id uuid NULL", "actor_tenant_id uuid NOT NULL",
            "reached_tenant_ids uuid[] NOT NULL", "resource text NOT NULL", "filters jsonb NULL", "procedure_id uuid NULL",
            "procedure_tenant_id uuid NULL", "attachment_id uuid NULL", "result text NOT NULL", "row_version bigint NOT NULL DEFAULT 0" })
        {
            sql.Should().Contain(col);
        }

        // Sin FK: la fila sobrevive al desvínculo (AC3).
        sql.Should().NotContain("REFERENCES identity.tenants").And.NotContain("REFERENCES tramites.procedure_instances");
        sql.Should().NotContain("ON DELETE CASCADE");

        // Append-only por trigger (como tenant_hierarchy_audit) y CHECKs.
        sql.Should().Contain("BEFORE UPDATE OR DELETE ON tramites.network_access_audit");
        sql.Should().Contain("ck_network_access_audit_reached_not_empty CHECK (cardinality(reached_tenant_ids) > 0)");
        sql.Should().Contain("ck_network_access_audit_result CHECK (result IN ('ok', 'forbidden', 'not_found'))");

        // Índices: dueño, actor y GIN sobre los alcanzados.
        sql.Should().Contain("ix_network_access_audit_procedure_tenant_occurred_at ON tramites.network_access_audit (procedure_tenant_id, occurred_at DESC)");
        sql.Should().Contain("ix_network_access_audit_actor_tenant_occurred_at ON tramites.network_access_audit (actor_tenant_id, occurred_at DESC)");
        sql.Should().Contain("USING gin (reached_tenant_ids)");

        // RLS por hijo dueño / hijo alcanzado / cabeza actora, sin FORCE (convención del repo).
        sql.Should().Contain("ENABLE ROW LEVEL SECURITY").And.NotContain("FORCE ROW LEVEL SECURITY");
        sql.Should().Contain("procedure_tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid");
        sql.Should().Contain("= ANY (reached_tenant_ids)");
        sql.Should().Contain("actor_tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid");

        // Comentarios en español sobre tabla y columnas.
        sql.Should().Contain("COMMENT ON TABLE tramites.network_access_audit IS");
        foreach (var col in new[] { "reached_tenant_ids", "filters", "result", "procedure_tenant_id", "attachment_id" })
            sql.Should().Contain($"COMMENT ON COLUMN tramites.network_access_audit.{col} IS");

        // La auditoría de lectura no lleva trg_audit_log: la propia tabla es la auditoría.
        sql.Should().NotContain("trg_audit_log");
    }

    [Fact]
    public void El_Down_retira_trigger_funcion_y_tabla_en_ese_orden()
    {
        var down = MigrationDownSql();
        var pasos = new[]
        {
            "DROP TRIGGER IF EXISTS tr_network_access_audit_immutable",
            "DROP FUNCTION IF EXISTS tramites.trg_network_access_audit_immutable",
            "DROP TABLE IF EXISTS tramites.network_access_audit",
        };
        pasos.Select(p => down.IndexOf(p, StringComparison.Ordinal)).Should().OnlyContain(i => i >= 0).And.BeInAscendingOrder();
    }

    [Fact]
    public void La_entidad_EF_mapea_la_tabla_como_el_DDL()
    {
        using var db = NewContext();
        var entidad = DesignModel(db).FindEntityType(typeof(NetworkAccessAuditEntry))!;

        entidad.GetSchema().Should().Be("tramites");
        entidad.GetTableName().Should().Be("network_access_audit");
        entidad.GetForeignKeys().Should().BeEmpty("sin FK a propósito: la fila sobrevive al desvínculo");
        entidad.FindProperty(nameof(NetworkAccessAuditEntry.ReachedTenantIds))!.GetColumnType().Should().Be("uuid[]");
        entidad.FindProperty(nameof(NetworkAccessAuditEntry.Filters))!.GetColumnType().Should().Be("jsonb");
        entidad.FindProperty(nameof(NetworkAccessAuditEntry.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entidad.GetIndexes().Select(i => i.GetDatabaseName()).Should().BeEquivalentTo(
            "ix_network_access_audit_procedure_tenant_occurred_at",
            "ix_network_access_audit_actor_tenant_occurred_at",
            "ix_network_access_audit_reached_tenant_ids");
        entidad.GetDeclaredTriggers().Select(t => t.GetDatabaseName()).Should().Equal("tr_network_access_audit_immutable");
    }

    [Fact]
    public void ElSnapshotNoTieneDriftSobreLaTabla()
    {
        using var db = NewContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var snapshotFinal = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var diferencias = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotFinal.GetRelationalModel(), DesignModel(db).GetRelationalModel());

        var sobreLaTabla = diferencias
            .Where(op => Describe(op).Contains("network_access_audit", StringComparison.Ordinal))
            .Select(op => $"{op.GetType().Name}: {Describe(op)}")
            .ToList();

        sobreLaTabla.Should().BeEmpty("el snapshot escrito a mano debe coincidir con la configuración EF");
    }

    private static string Describe(MigrationOperation op)
    {
        var tabla = op is ITableMigrationOperation t ? $"{t.Schema}.{t.Table}" : string.Empty;
        var nombre = op.GetType().GetProperty("Name")?.GetValue(op) as string;
        return string.IsNullOrEmpty(nombre) ? tabla : $"{tabla}.{nombre}";
    }
}
