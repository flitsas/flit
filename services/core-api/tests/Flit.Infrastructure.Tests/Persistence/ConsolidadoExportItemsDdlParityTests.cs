using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13368 — el DDL 134 (partes, ítems y auditoría del lote), las constantes del dominio y el modelo EF no se
/// desalinean: cada CHECK de vocabulario lista exactamente los valores del código (los diez códigos de omisión en el
/// orden de <see cref="ConsolidadoLoteOmisiones"/>), los índices parciales coinciden con el snapshot, la FK compuesta
/// apunta a la clave alterna de la parte, la auditoría no tiene navegaciones y la migración carga el DDL embebido con
/// un Down en orden inverso. El comportamiento contra el motor lo cubre
/// <c>ConsolidadoExportItemsSchemaMigrationTests</c> en Flit.Integration.Tests.
/// </summary>
public sealed partial class ConsolidadoExportItemsDdlParityTests
{
    private const string DdlFile = "134-HU13368-consolidado-export-items.sql";
    private const string MigrationTypeName = "Flit.Infrastructure.Migrations.HU13368_ConsolidadoExportItems";

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

    private static string LoadDdlConComentarios()
    {
        const string resource = "Flit.Infrastructure.Persistence.Sql.Ddl." + DdlFile;
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Migration NewMigration()
    {
        var tipo = typeof(FlitDbContext).Assembly.GetType(MigrationTypeName);
        tipo.Should().NotBeNull($"la migración {MigrationTypeName} debe existir (dotnet ef migrations add)");
        return (Migration)Activator.CreateInstance(tipo!)!;
    }

    private static FlitDbContext NewModelContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(nameof(ConsolidadoExportItemsDdlParityTests)).Options);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Blancos();

    /// <summary>Valores literales, en orden, del <c>IN (…)</c> del CHECK <paramref name="check"/>.</summary>
    private static List<string> ValoresDelCheck(string ddl, string check)
    {
        var inicio = ddl.IndexOf(check + " CHECK", StringComparison.Ordinal);
        inicio.Should().BeGreaterThanOrEqualTo(0, $"el DDL debe declarar {check}");
        var cuerpo = ddl[inicio..];
        cuerpo = cuerpo[..cuerpo.IndexOf("))", StringComparison.Ordinal)];
        return cuerpo[cuerpo.IndexOf(" IN (", StringComparison.Ordinal)..].Split('\'').Where((_, i) => i % 2 == 1).ToList();
    }

    private static string Normalizado(string sql) => Blancos().Replace(sql, " ").Trim();

    [Fact]
    public void LosEstadosDeParteEItemCoincidenConElCodigo_IncluidosLosDeCancelacion()
    {
        var ddl = LoadDdl();

        ValoresDelCheck(ddl, "ck_consolidado_export_batch_parts_status").Should()
            .Equal(ConsolidadoExportPartStatus.Todos, "el CHECK de partes no admite estados que el código no conozca");
        ValoresDelCheck(ddl, "ck_consolidado_export_batch_items_status").Should()
            .Equal(ConsolidadoExportItemStatus.Todos);
        ValoresDelCheck(ddl, "ck_consolidado_export_batch_items_delivery_mode").Should()
            .Equal(ConsolidadoExportDeliveryMode.Todos);

        ConsolidadoExportPartStatus.Todos.Should().Contain("descartada", "AC6");
        ConsolidadoExportItemStatus.Todos.Should().Contain("cancelado", "AC6");
    }

    [Fact]
    public void ElCheckDeOmisionListaLosDiezCodigosDeConsolidadoLoteOmisionesEnSuOrden()
    {
        var codigos = ValoresDelCheck(LoadDdl(), "ck_consolidado_export_batch_items_omission_code");

        codigos.Should().HaveCount(10);
        codigos.Should().Equal(ConsolidadoLoteOmisiones.Todos,
            "el CHECK es contrato con la clase de #13371: mismos códigos, literales y en el mismo orden");
        codigos.Should().NotContain(["consolidado_no_generado", "en_regeneracion"], "eliminados en v2");
    }

    [Fact]
    public void LosVocabulariosDeLaAuditoriaCoincidenConElCodigo()
    {
        var ddl = LoadDdl();

        ValoresDelCheck(ddl, "ck_consolidado_export_audit_event").Should().Equal(ConsolidadoExportAuditEvent.Todos);
        ValoresDelCheck(ddl, "ck_consolidado_export_audit_origin").Should().Equal(ConsolidadoExportOrigin.Todos);
        ValoresDelCheck(ddl, "ck_consolidado_export_audit_document_type").Should().Equal(ConsolidadoExportDocumentType.Todos);
        ValoresDelCheck(ddl, "ck_consolidado_export_audit_selection_mode").Should().Equal(ConsolidadoExportSelectionMode.Todos);
    }

    [Fact]
    public void LoteCanceladoExigeConteos_AC6()
    {
        Normalizado(LoadDdl()).Should().Contain(
            "CONSTRAINT ck_consolidado_export_audit_cancelled CHECK ( event <> 'lote_cancelado' OR (total_items IS NOT NULL "
            + "AND included_count IS NOT NULL AND omitted_count IS NOT NULL AND generated_count IS NOT NULL "
            + "AND generated_count <= included_count))");
    }

    [Fact]
    public void LaAuditoriaEsAppendOnlyConLaPoliticaDelDdl113YGin()
    {
        var ddl = Normalizado(LoadDdl());

        ddl.Should().Contain("CREATE TRIGGER tr_consolidado_export_audit_immutable BEFORE UPDATE OR DELETE ON tramites.consolidado_export_audit");
        ddl.Should().Contain("ON tramites.consolidado_export_audit USING gin (reached_tenant_ids)");
        ddl.Should().Contain("OR actor_tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid "
                             + "OR NULLIF(current_setting('app.current_tenant_id', true), '')::uuid = ANY (reached_tenant_ids)");
        ddl.Should().NotContain("tr_consolidado_export_audit_row_version", "append-only: ninguna fila cambia de versión");
    }

    [Fact]
    public void LaPiiQuedaEtiquetadaYLaPurgaNoAnulaLaPlaca()
    {
        var ddl = LoadDdlConComentarios();

        ddl.Should().Contain("COMMENT ON COLUMN tramites.consolidado_export_batch_items.plate IS '@pii:low");
        ddl.Should().Contain("COMMENT ON COLUMN tramites.consolidado_export_audit.client_ip IS '@pii:medium");
        LoadDdl().Should().NotContain("plate IS NULL", "S2 = b): ningún CHECK asume la placa anulada por la purga")
            .And.NotContain("plate = NULL");
    }

    [Fact]
    public void ElDdlSoloCreaLasTresTablasDeEstaHu_PartesAntesQueItems()
    {
        var ddl = LoadDdl();

        var tablas = ddl.Split("CREATE TABLE IF NOT EXISTS ", StringSplitOptions.None).Skip(1)
            .Select(t => t[..t.IndexOf(' ', StringComparison.Ordinal)]).ToList();
        tablas.Should().Equal(
            "tramites.consolidado_export_batch_parts",
            "tramites.consolidado_export_batch_items",
            "tramites.consolidado_export_audit");
        ddl.Should().NotContain("BEGIN;").And.NotContain("COMMIT;");
    }

    [Fact]
    public void LaMigracionCargaElDdlEmbebidoYElDownBorraEnOrdenInverso()
    {
        var migracion = NewMigration();

        var up = migracion.UpOperations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>().Subject;
        up.Sql.Should().Contain("CREATE TABLE IF NOT EXISTS tramites.consolidado_export_audit");

        var down = migracion.DownOperations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>().Subject;
        down.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Should().Equal(
            "DROP TABLE IF EXISTS tramites.consolidado_export_audit",
            "DROP FUNCTION IF EXISTS tramites.trg_consolidado_export_audit_immutable()",
            "DROP TABLE IF EXISTS tramites.consolidado_export_batch_items",
            "DROP TABLE IF EXISTS tramites.consolidado_export_batch_parts",
            "DROP FUNCTION IF EXISTS tramites.trg_consolidado_export_batch_parts_tenant()");
    }

    [Fact]
    public void LosIndicesParcialesDelModeloSonLosDelDdl()
    {
        var ddl = Normalizado(LoadDdl());
        using var db = NewModelContext();

        foreach (var (tipo, indice) in new (Type, string)[]
                 {
                     (typeof(ConsolidadoExportBatchPart), "ix_consolidado_export_batch_parts_claim"),
                     (typeof(ConsolidadoExportBatchItem), "ix_consolidado_export_batch_items_batch_part"),
                     (typeof(ConsolidadoExportBatchItem), "ix_consolidado_export_batch_items_claim"),
                     (typeof(ConsolidadoExportBatchItem), "ix_consolidado_export_batch_items_unassigned"),
                 })
        {
            var filtro = db.Model.FindEntityType(tipo)!.GetIndexes().Single(i => i.GetDatabaseName() == indice).GetFilter();
            filtro.Should().NotBeNullOrEmpty();
            ddl.Should().MatchRegex($@"CREATE INDEX IF NOT EXISTS {indice} ON [^;]*WHERE {Regex.Escape(filtro!)};",
                $"{indice} tiene el mismo filtro en el DDL y en el snapshot");
        }

        var activos = $"status IN ({string.Join(", ", ConsolidadoExportItemStatus.Vivos.Select(v => $"'{v}'"))})";
        ddl.Should().Contain($"WHERE {activos};", "el reclamo usa exactamente los estados vivos");
    }

    [Fact]
    public void ElModeloEfMapeaLaFkCompuestaYLaClaveAlternaDeLaParte()
    {
        using var db = NewModelContext();
        var item = db.Model.FindEntityType(typeof(ConsolidadoExportBatchItem))!;
        var parte = db.Model.FindEntityType(typeof(ConsolidadoExportBatchPart))!;

        item.GetTableName().Should().Be("consolidado_export_batch_items");
        parte.GetTableName().Should().Be("consolidado_export_batch_parts");

        var fk = item.GetForeignKeys().Single(f => f.GetConstraintName() == "fk_consolidado_export_batch_items_batch_parts");
        fk.Properties.Select(p => p.Name).Should().Equal(
            nameof(ConsolidadoExportBatchItem.BatchId), nameof(ConsolidadoExportBatchItem.PartNumber));
        fk.PrincipalKey.Properties.Select(p => p.Name).Should().Equal(
            nameof(ConsolidadoExportBatchPart.BatchId), nameof(ConsolidadoExportBatchPart.PartNumber));
        fk.PrincipalKey.GetName().Should().Be("uq_consolidado_export_batch_parts_batch_number");
        fk.DeleteBehavior.Should().Be(DeleteBehavior.NoAction);

        item.GetForeignKeys().Single(f => f.GetConstraintName() == "fk_consolidado_export_batch_items_batches")
            .DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
        parte.GetForeignKeys().Single(f => f.GetConstraintName() == "fk_consolidado_export_batch_parts_batches")
            .DeleteBehavior.Should().Be(DeleteBehavior.Cascade);

        var unico = item.GetIndexes().Single(i => i.GetDatabaseName() == "uq_consolidado_export_batch_items_batch_instance");
        unico.IsUnique.Should().BeTrue("AC4");

        foreach (var (entidad, prop, nulable) in new (Microsoft.EntityFrameworkCore.Metadata.IEntityType, string, bool)[]
                 {
                     (item, nameof(ConsolidadoExportBatchItem.TenantId), false),
                     (item, nameof(ConsolidadoExportBatchItem.CreatedBy), false),
                     (item, nameof(ConsolidadoExportBatchItem.ReferenceNumber), false),
                     (item, nameof(ConsolidadoExportBatchItem.Plate), true),
                     (item, nameof(ConsolidadoExportBatchItem.PartNumber), true),
                     (item, nameof(ConsolidadoExportBatchItem.OmissionCode), true),
                     (parte, nameof(ConsolidadoExportBatchPart.TenantId), true),
                     (parte, nameof(ConsolidadoExportBatchPart.NoncePrefix), true),
                 })
        {
            entidad.FindProperty(prop)!.IsNullable.Should().Be(nulable, $"{entidad.ClrType.Name}.{prop}");
        }

        item.FindProperty(nameof(ConsolidadoExportBatchItem.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        parte.FindProperty(nameof(ConsolidadoExportBatchPart.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void LaAuditoriaNoTieneNavegacionesNiFkYMapeaSusTipos()
    {
        using var db = NewModelContext();
        var audit = db.Model.FindEntityType(typeof(ConsolidadoExportAuditEntry))!;

        audit.GetTableName().Should().Be("consolidado_export_audit");
        audit.GetSchema().Should().Be("tramites");
        audit.GetForeignKeys().Should().BeEmpty("E2: la fila sobrevive al lote, al usuario y a la compañía");
        audit.GetNavigations().Should().BeEmpty();
        audit.GetReferencingForeignKeys().Should().BeEmpty();

        // Anotación explícita: el modelo InMemory no resuelve mapeos relacionales con GetColumnType().
        foreach (var (prop, tipo) in new[]
                 {
                     (nameof(ConsolidadoExportAuditEntry.ReachedTenantIds), "uuid[]"),
                     (nameof(ConsolidadoExportAuditEntry.ClientIp), "inet"),
                     (nameof(ConsolidadoExportAuditEntry.FilterSummary), "jsonb"),
                 })
        {
            (audit.FindProperty(prop)!.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value).Should().Be(tipo, prop);
        }

        audit.FindProperty(nameof(ConsolidadoExportAuditEntry.ActorTenantId))!.IsNullable.Should().BeTrue("Q8");
        audit.GetIndexes().Single(i => i.GetDatabaseName() == "ix_consolidado_export_audit_reached_tenant_ids")
            .GetMethod().Should().Be("gin");
    }
}
