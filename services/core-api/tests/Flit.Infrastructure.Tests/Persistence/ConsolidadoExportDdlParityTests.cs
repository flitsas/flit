using System.Text;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13367 — el DDL 133, las constantes del dominio y el modelo EF no se desalinean: cada estado, origen, tipo de
/// documento y modo de selección del código está en su CHECK, el índice de lote activo usa exactamente los estados
/// activos, la migración carga el DDL embebido y el modelo mapea las columnas con su nulabilidad. El comportamiento
/// contra el motor lo cubre <c>ConsolidadoExportSchemaMigrationTests</c> en Flit.Integration.Tests.
/// </summary>
public sealed class ConsolidadoExportDdlParityTests
{
    private const string DdlFile = "133-HU13367-consolidado-export-batches.sql";

    private static string LoadDdl()
    {
        const string resource = "Flit.Infrastructure.Persistence.Sql.Ddl." + DdlFile;
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(l =>
        {
            var corte = l.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? l[..corte] : l;
        }));
    }

    private static FlitDbContext NewModelContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(nameof(ConsolidadoExportDdlParityTests)).Options);

    private static string InList(IEnumerable<string> valores) => string.Join(", ", valores.Select(v => $"'{v}'"));

    [Fact]
    public void LosChecksDelLoteListanExactamenteLosValoresDelCodigo()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain($"ck_consolidado_export_batches_origin CHECK (origin IN ({InList(ConsolidadoExportOrigin.Todos)}))");
        ddl.Should().Contain(
            $"ck_consolidado_export_batches_document_type CHECK (document_type IN ({InList(ConsolidadoExportDocumentType.Todos)}))");
        ddl.Should().Contain(
            $"ck_consolidado_export_batches_selection_mode CHECK (selection_mode IN ({InList(ConsolidadoExportSelectionMode.Todos)}))");

        var status = ddl[ddl.IndexOf("ck_consolidado_export_batches_status", StringComparison.Ordinal)..];
        status = status[..status.IndexOf("))", StringComparison.Ordinal)];
        foreach (var estado in ConsolidadoExportStatus.Todos)
        {
            status.Should().Contain($"'{estado}'");
        }

        status.Split('\'').Where((_, i) => i % 2 == 1).Should().BeEquivalentTo(ConsolidadoExportStatus.Todos,
            "el CHECK no admite estados que el código no conozca");
    }

    [Fact]
    public void ElIndiceDeLoteActivoYLaMaquinaDeEstadosUsanLosEstadosActivosDelCodigo()
    {
        var ddl = LoadDdl();
        var activos = $"status IN ({InList(ConsolidadoExportStatus.Activos)})";

        ddl.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_consolidado_export_batches_active_per_user")
            .And.Contain($"WHERE {activos} AND deleted_at IS NULL");
        ddl.Should().Contain($"({activos}) = (finished_at IS NULL)", "ck_consolidado_export_batches_finished");

        using var db = NewModelContext();
        var indice = db.Model.FindEntityType(typeof(ConsolidadoExportBatch))!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "uq_consolidado_export_batches_active_per_user");
        indice.IsUnique.Should().BeTrue();
        indice.GetFilter().Should().Be($"{activos} AND deleted_at IS NULL", "el snapshot refleja el mismo índice parcial");
    }

    [Fact]
    public void LosChecksDeOrigenSonBicondicionales_E5YRd()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain(
            "ck_consolidado_export_batches_tenant_origin CHECK ((origin = 'superadmin') = (tenant_id IS NULL))");
        ddl.Should().Contain(
            "ck_consolidado_export_batches_ot_origin CHECK ((origin = 'ot_bandeja') = (ot_transit_office_id IS NOT NULL))");
        ddl.Should().Contain("ck_consolidado_export_batches_scope_origin CHECK (scope_tenant_id IS NULL OR origin = 'superadmin')");
    }

    [Fact]
    public void LosParametrosNacenConLosValoresDelAcYLaFilaEsUnica()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("max_pdfs_per_part     integer     NOT NULL DEFAULT 500")
            .And.Contain("max_mb_per_part       integer     NOT NULL DEFAULT 250")
            .And.Contain("item_slots            smallint    NOT NULL DEFAULT 2")
            .And.Contain("retry_delay_seconds   integer     NOT NULL DEFAULT 30")
            .And.Contain("item_timeout_seconds  integer     NOT NULL DEFAULT 300")
            .And.Contain("item_lease_seconds    integer     NOT NULL DEFAULT 600")
            .And.Contain("retention_hours       integer     NOT NULL DEFAULT 24")
            .And.Contain("max_items_per_batch   integer     NOT NULL DEFAULT 10000");
        ddl.Should().Contain("ON tramites.consolidado_export_settings ((true))");
        ddl.Should().Contain("WHERE NOT EXISTS (SELECT 1 FROM tramites.consolidado_export_settings)", "sembrado reproducible");
    }

    [Fact]
    public void ElDdlSoloCreaLasDosTablasDeEstaHu_SinPartesItemsNiAuditoria()
    {
        var ddl = LoadDdl();

        var tablas = ddl.Split("CREATE TABLE IF NOT EXISTS ", StringSplitOptions.None).Skip(1)
            .Select(t => t[..t.IndexOf(' ', StringComparison.Ordinal)]).ToList();
        tablas.Should().BeEquivalentTo("tramites.consolidado_export_settings", "tramites.consolidado_export_batches");
        ddl.Should().NotContain("omission_code", "los códigos de omisión son de HU #13371");
        ddl.Should().NotContain("BEGIN;").And.NotContain("COMMIT;");
    }

    /// <summary>
    /// H1 (security-agent, ADR-0070 adenda v6): <c>public.trg_audit_log()</c> copia <c>to_jsonb(NEW/OLD)</c> completo a
    /// <c>audit.audit_logs</c>; sobre el lote copiaría <c>dek_wrapped</c> y anularía la purga criptográfica (Q5). El
    /// lote no lleva ese trigger (su traza Ley 1581 es <c>consolidado_export_audit</c>); los parámetros sí lo conservan.
    /// </summary>
    [Fact]
    public void H1_ElLoteNoLlevaLaAuditoriaGenerica_ParaQueLaDekEnvueltaNoSalgaDeLaTabla()
    {
        var ddl = LoadDdl();

        var triggersDelLote = System.Text.RegularExpressions.Regex.Matches(
                ddl, @"CREATE TRIGGER\s+(\w+)[^;]*?ON tramites\.consolidado_export_batches[^;]*;")
            .Select(m => m.Value).ToList();
        triggersDelLote.Should().ContainSingle("solo queda el de row_version")
            .Which.Should().Contain("tr_consolidado_export_batches_row_version").And.NotContain("trg_audit_log");
        ddl.Should().NotContain("tr_consolidado_export_batches_audit");
        ddl.Should().Contain("CREATE TRIGGER tr_consolidado_export_settings_audit",
            "el histórico de calibraciones sigue en audit.audit_logs (A3.4)");

        var comentario = ddl[ddl.IndexOf("COMMENT ON COLUMN tramites.consolidado_export_batches.dek_wrapped", StringComparison.Ordinal)..];
        comentario = comentario[..comentario.IndexOf("';", StringComparison.Ordinal)];
        comentario.Should().ContainEquivalentOf("nunca sale a la auditoría genérica").And.Contain("audit.audit_logs");
    }

    [Fact]
    public void LaMigracionCargaElDdlEmbebidoYElDownBorraSoloSusTablas()
    {
        var migracion = new HU13367_ConsolidadoExportBatches();

        var up = migracion.UpOperations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>().Subject;
        up.Sql.Should().Contain("CREATE TABLE IF NOT EXISTS tramites.consolidado_export_batches");

        var down = migracion.DownOperations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>().Subject;
        var sentencias = down.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        sentencias.Should().BeEquivalentTo(
            "DROP TABLE IF EXISTS tramites.consolidado_export_batches",
            "DROP TABLE IF EXISTS tramites.consolidado_export_settings");
    }

    [Fact]
    public void ElModeloEfMapeaElLoteConSuNulabilidad()
    {
        using var db = NewModelContext();
        var entity = db.Model.FindEntityType(typeof(ConsolidadoExportBatch))!;

        entity.GetSchema().Should().Be("tramites");
        entity.GetTableName().Should().Be("consolidado_export_batches");

        var nulables = new Dictionary<string, string>
        {
            [nameof(ConsolidadoExportBatch.TenantId)] = "tenant_id",
            [nameof(ConsolidadoExportBatch.ScopeTenantId)] = "scope_tenant_id",
            [nameof(ConsolidadoExportBatch.OtTransitOfficeId)] = "ot_transit_office_id",
            [nameof(ConsolidadoExportBatch.DekWrapped)] = "dek_wrapped",
            [nameof(ConsolidadoExportBatch.FinishedAt)] = "finished_at",
            [nameof(ConsolidadoExportBatch.ExpiresAt)] = "expires_at",
            [nameof(ConsolidadoExportBatch.PurgedAt)] = "purged_at",
            [nameof(ConsolidadoExportBatch.DeletedAt)] = "deleted_at",
        };
        foreach (var (propiedad, columna) in nulables)
        {
            var p = entity.FindProperty(propiedad)!;
            p.GetColumnName().Should().Be(columna);
            p.IsNullable.Should().BeTrue($"{columna} es NULL en el DDL 133");
        }

        foreach (var (propiedad, columna) in new Dictionary<string, string>
        {
            [nameof(ConsolidadoExportBatch.RequestedByUserId)] = "requested_by_user_id",
            [nameof(ConsolidadoExportBatch.Origin)] = "origin",
            [nameof(ConsolidadoExportBatch.Status)] = "status",
            [nameof(ConsolidadoExportBatch.EffectsAcknowledgedAt)] = "effects_acknowledged_at",
            [nameof(ConsolidadoExportBatch.CreatedBy)] = "created_by",
        })
        {
            var p = entity.FindProperty(propiedad)!;
            p.GetColumnName().Should().Be(columna);
            p.IsNullable.Should().BeFalse($"{columna} es NOT NULL en el DDL 133");
        }

        entity.FindProperty(nameof(ConsolidadoExportBatch.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void ElModeloEfMapeaLosParametros()
    {
        using var db = NewModelContext();
        var entity = db.Model.FindEntityType(typeof(ConsolidadoExportSettings))!;

        entity.GetTableName().Should().Be("consolidado_export_settings");
        var columnas = new Dictionary<string, string>
        {
            [nameof(ConsolidadoExportSettings.MaxPdfsPerPart)] = "max_pdfs_per_part",
            [nameof(ConsolidadoExportSettings.MaxMbPerPart)] = "max_mb_per_part",
            [nameof(ConsolidadoExportSettings.ItemSlots)] = "item_slots",
            [nameof(ConsolidadoExportSettings.ItemTimeoutSeconds)] = "item_timeout_seconds",
            [nameof(ConsolidadoExportSettings.ItemLeaseSeconds)] = "item_lease_seconds",
            [nameof(ConsolidadoExportSettings.RetryDelaySeconds)] = "retry_delay_seconds",
            [nameof(ConsolidadoExportSettings.RetentionHours)] = "retention_hours",
            [nameof(ConsolidadoExportSettings.IsActive)] = "is_active",
            [nameof(ConsolidadoExportSettings.MaxItemsPerBatch)] = "max_items_per_batch",
        };
        foreach (var (propiedad, columna) in columnas)
        {
            entity.FindProperty(propiedad)!.GetColumnName().Should().Be(columna);
        }

        entity.FindProperty(nameof(ConsolidadoExportSettings.MaxItemsPerBatch))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(ConsolidadoExportSettings.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
    }

    /// <summary>
    /// M1 (épica #13216) — tope total del lote: en sitio en el DDL 133 (migración Pending), con CHECK nombrado de
    /// rango y el mismo DEFAULT que la constante de la entidad.
    /// </summary>
    [Fact]
    public void M1_ElTopeTotalDelLoteViveEnElDdl133ConCheckDeRangoYElDefaultDeLaEntidad()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain(
            "CONSTRAINT ck_consolidado_export_settings_max_items CHECK (max_items_per_batch BETWEEN 1 AND 50000)");
        ddl.Should().Contain("COMMENT ON COLUMN tramites.consolidado_export_settings.max_items_per_batch");
        ddl.Should().Contain($"DEFAULT {ConsolidadoExportSettings.MaxItemsPerBatchPorDefecto}");
    }
}
