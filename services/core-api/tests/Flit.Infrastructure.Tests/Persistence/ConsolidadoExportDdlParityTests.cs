using System.Text;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
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
        // HU #13417 (adenda v7): el scope es del Super Admin o la hija acotada de un lote de red.
        ddl.Should().Contain(
            "ck_consolidado_export_batches_scope_origin CHECK (scope_tenant_id IS NULL OR origin = 'superadmin' OR (origin = 'tramites' AND network_scope))");
    }

    /// <summary>
    /// HU #13417 (ADR-0070 adenda v7) — lote desde la vista de red: <c>network_scope</c> nace falso, solo existe en
    /// origen <c>tramites</c> y la hija acotada nunca es la propia compañía del lote; el modelo EF lo mapea NOT NULL.
    /// </summary>
    [Fact]
    public void HU13417_ElLoteDeRedTieneSuColumnaYSusChecks_YElModeloLaMapea()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("network_scope           boolean     NOT NULL DEFAULT false");
        ddl.Should().Contain("ck_consolidado_export_batches_network_origin CHECK (NOT network_scope OR origin = 'tramites')");
        ddl.Should().Contain(
            "ck_consolidado_export_batches_network_child CHECK (scope_tenant_id IS NULL OR tenant_id IS NULL OR scope_tenant_id <> tenant_id)");

        using var db = NewModelContext();
        var p = db.Model.FindEntityType(typeof(ConsolidadoExportBatch))!.FindProperty(nameof(ConsolidadoExportBatch.NetworkScope))!;
        p.GetColumnName().Should().Be("network_scope");
        p.IsNullable.Should().BeFalse();
        new ConsolidadoExportBatch().NetworkScope.Should().BeFalse("el lote de siempre no es de red");
    }

    /// <summary>
    /// HU #13417 — las dos migraciones Pending se regeneraron con la herramienta (no a mano): sus Designer y el snapshot
    /// conocen <c>NetworkScope</c>, y las dos siguen cargando su DDL embebido (133 con la columna, 134 con el código).
    /// </summary>
    [Fact]
    public void HU13417_LasMigracionesRegeneradasConocenNetworkScopeEnDesignerYSnapshot()
    {
        static bool ConoceRed(IReadOnlyModel? modelo) =>
            modelo?.FindEntityType(typeof(ConsolidadoExportBatch).FullName!)?.FindProperty(nameof(ConsolidadoExportBatch.NetworkScope))
                is not null;

        var lote = new HU13367_ConsolidadoExportBatches();
        var items = new HU13368_ConsolidadoExportItems();
        foreach (var (migracion, sufijo, ddl) in new (Migration, string, string)[]
                 {
                     (lote, "_HU13367_ConsolidadoExportBatches", "network_scope"),
                     (items, "_HU13368_ConsolidadoExportItems", "red_sin_consolidado"),
                 })
        {
            migracion.GetType().GetCustomAttributes(typeof(MigrationAttribute), false).Cast<MigrationAttribute>()
                .Should().ContainSingle().Which.Id.Should().EndWith(sufijo);
            ConoceRed(migracion.TargetModel).Should().BeTrue($"el Designer de {sufijo} regenerado incluye network_scope");
            migracion.UpOperations.OfType<SqlOperation>().Single().Sql.Should().Contain(ddl);
        }

        var snapshot = (ModelSnapshot)Activator.CreateInstance(
            typeof(FlitDbContext).Assembly.GetType("Flit.Infrastructure.Migrations.FlitDbContextModelSnapshot")!, nonPublic: true)!;
        ConoceRed(snapshot.Model).Should().BeTrue("el snapshot incluye network_scope");
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
            "CONSTRAINT ck_consolidado_export_settings_max_items CHECK (max_items_per_batch BETWEEN 1 AND "
            + $"{ConsolidadoExportSettings.MaxItemsPerBatchMaximo})");
        ddl.Should().Contain("COMMENT ON COLUMN tramites.consolidado_export_settings.max_items_per_batch");
        ddl.Should().Contain($"DEFAULT {ConsolidadoExportSettings.MaxItemsPerBatchPorDefecto}");
    }

    /// <summary>
    /// Code review épica #13216 (Obs1) — el número de parte es <c>smallint</c> y un lote tiene como mucho
    /// <c>ítems + 1</c> partes (una por ítem si cada PDF supera M, más la parte 0/0 de un lote sin partes), así que el
    /// tope del DDL 133 es <c>short.MaxValue - 1</c>, sin el viejo 50.000 en el CHECK ni en el comentario de la columna.
    /// </summary>
    [Fact]
    public void Obs1_ElTopeDelDdl133NoDesbordaElSmallintDelNumeroDeParte()
    {
        var ddl = LoadDdl();

        (ConsolidadoExportSettings.MaxItemsPerBatchMaximo + 1).Should().Be(short.MaxValue, "ítems + la parte 0/0");
        ConsolidadoExportSettings.MaxItemsPerBatchPorDefecto.Should().BeLessThanOrEqualTo(ConsolidadoExportSettings.MaxItemsPerBatchMaximo);
        ddl.Should().Contain("BETWEEN 1 AND 32766").And.NotContain("50000").And.NotContain("50.000");
        ddl.Should().Contain("1–32.766");
    }
}
