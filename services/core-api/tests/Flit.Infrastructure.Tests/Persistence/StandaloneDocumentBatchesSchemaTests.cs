using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12210 (Feature #12201, I3) — esquema de lotes: <c>admin.standalone_document_batches</c> y la
/// ampliación de <c>admin.standalone_documents</c> con el vínculo al lote.
///
/// <para><b>Verificación estática del DDL embebido: en esta suite no hay Postgres.</b> Se comprueba
/// que el script dice lo que debe decir. Las pruebas del modelo EF sí interrogan el modelo real: son
/// las que atrapan un nombre de columna equivocado, que reventaría en runtime y no al compilar.</para>
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// Statements().Should().Contain("CREATE TABLE IF NOT EXISTS admin.standalone_document_batches");
/// </code>
/// </summary>
public sealed class StandaloneDocumentBatchesSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.106-F12201-generacion-documental-lotes.sql";

    private static string Load()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>El script sin comentarios: lo que el motor realmente ejecuta.</summary>
    private static string Statements() => Regex.Replace(Load(), @"(?m)^\s*--.*$", string.Empty);

    // ── El par de migración de I3 viaja en ESTA HU (cierre del aviso operativo) ─────────────────

    [Fact]
    public void LaMigracionDeI3EjecutaElDdl106YNoElDeI1()
    {
        var migracion = File.ReadAllText(
            MigrationPath("20260909130000_F12201_GeneracionDocumentalLotes.cs"));

        migracion.Should().Contain("EmbeddedDdl.LoadUp(\"106-F12201-generacion-documental-lotes.sql\")");
        migracion.Should().NotContain("105-F12201-generacion-documental.sql");
    }

    [Fact]
    public void ElDownRevierteElVinculoYRestauraLaFuncionDelDdl105()
    {
        var migracion = File.ReadAllText(
            MigrationPath("20260909130000_F12201_GeneracionDocumentalLotes.cs"));

        // Sin restaurar la función, el Down dejaría un trigger comparando columnas que ya no existen.
        migracion.Should().Contain("DROP TABLE IF EXISTS admin.standalone_document_batches");
        migracion.Should().Contain("DROP COLUMN IF EXISTS validation_errors");
        migracion.Should().Contain("DROP COLUMN IF EXISTS row_number");
        migracion.Should().Contain("DROP COLUMN IF EXISTS batch_id");
        migracion.Should().Contain("CREATE OR REPLACE FUNCTION admin.trg_standalone_document_immutable()");
    }

    // ── Reglas transversales del Feature ───────────────────────────────────────────────────────

    [Fact]
    public void ElXlsxFuenteNoViveEnLaFilaYNoHayFkHaciaTramites()
    {
        var sql = Statements();

        sql.Should().NotMatchRegex(@"(?i)\bbytea\b", "el XLSX fuente vive en storage, como los PDF");
        sql.Should().Contain("source_storage_path");
        sql.Should().Contain("source_sha256");
        sql.Should().NotContain("tramites.");
        sql.Should().NotMatchRegex(@"(?i)procedure_instance");
    }

    [Fact]
    public void ElDdlEsIdempotenteYSinTransaccionPropia()
    {
        var sql = Statements();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
        sql.Should().NotMatchRegex(@"CREATE TABLE (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"CREATE (UNIQUE )?INDEX (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"(?im)^\s*BEGIN\s*;");
        sql.Should().NotMatchRegex(@"(?im)^\s*COMMIT\s*;");
    }

    // ── Los topes y las guardas que sostienen los AC ───────────────────────────────────────────

    [Fact]
    public void ElTopeDeCienFilasEstaEnLaBaseDeDatos()
    {
        // CF-11 no es solo una validación del parser: el CHECK impide que un lote de 101 filas
        // exista aunque alguien invoque el repositorio saltándose el handler.
        Statements().Should().Contain("CHECK (total_items BETWEEN 0 AND 100)");
    }

    [Fact]
    public void UnLoteCerradoNoPuedeQuedarSinFechaDeCierre()
    {
        Statements().Should().Contain("ck_standalone_document_batches_cierre");
    }

    [Fact]
    public void LaIdempotenciaDelLoteEsUnUnicoParcialPorTenant()
    {
        var sql = Statements();

        sql.Should().Contain("uq_standalone_document_batches_tenant_idempotency");
        sql.Should().Contain("WHERE idempotency_key IS NOT NULL AND deleted_at IS NULL");
    }

    [Fact]
    public void ElIndiceUnicoPorLoteYFilaEsLoQueImpideGenerarDosVecesLaMismaFila()
    {
        // R5 — es el respaldo duro del reproceso tras el reaper. Sin él, un lote atascado que se
        // reclama dos veces podría emitir el mismo documento dos veces.
        var sql = Statements();

        sql.Should().Contain("uq_standalone_documents_batch_row");
        sql.Should().Contain("ON admin.standalone_documents (batch_id, row_number)");
    }

    [Fact]
    public void BatchIdYRowNumberViajanJuntosONoViajan()
    {
        Statements().Should().Contain("ck_standalone_documents_batch_row");
    }

    [Fact]
    public void ElTriggerCongelaElVinculoConElLotePeroNoLosErroresDeLaFila()
    {
        var sql = Statements();

        // Capa 2 — la identidad de la fila incluye el vínculo con el lote desde el DDL 106.
        sql.Should().Contain("NEW.batch_id           IS DISTINCT FROM OLD.batch_id");
        sql.Should().Contain("NEW.row_number         IS DISTINCT FROM OLD.row_number");

        // validation_errors NO se congela: el worker la escribe después de dejar la fila en error.
        var funcion = sql[sql.IndexOf(
            "CREATE OR REPLACE FUNCTION admin.trg_standalone_document_immutable()",
            StringComparison.Ordinal)..];
        funcion.Should().NotContain("NEW.validation_errors");

        // Y CF-19 sigue vivo: la auditoría de descarga tampoco entra en ninguna comparación.
        funcion.Should().NotContain("NEW.downloaded_at");
        funcion.Should().NotContain("NEW.download_count");
    }

    [Fact]
    public void LaPoliticaRlsExisteYNoEsUnControl()
    {
        var sql = Statements();

        sql.Should().Contain("CREATE POLICY tenant_isolation ON admin.standalone_document_batches");
        sql.Should().NotContain("FORCE ROW LEVEL SECURITY");
        Load().Should().Contain("RLS decorativa");
    }

    // ── Modelo EF — aquí sí se verifica comportamiento ─────────────────────────────────────────

    [Fact]
    public void LaEntidadDeLoteMapeaLaTablaDelSchemaAdminYQuedaFueraDeLasMigraciones()
    {
        using var db = NewContext();

        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(StandaloneDocumentBatchEntity));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("standalone_document_batches");
        entity!.GetSchema().Should().Be("admin");
        entity!.IsTableExcludedFromMigrations().Should().BeTrue(
            "el DDL lo lleva el SQL crudo 106; si EF también lo generara habría dos fuentes del esquema");
    }

    [Theory]
    [InlineData(nameof(StandaloneDocumentBatchEntity.CreatedByUserId), "created_by_user_id")]
    [InlineData(nameof(StandaloneDocumentBatchEntity.SourceStoragePath), "source_storage_path")]
    [InlineData(nameof(StandaloneDocumentBatchEntity.SourceSha256), "source_sha256")]
    [InlineData(nameof(StandaloneDocumentBatchEntity.TotalItems), "total_items")]
    [InlineData(nameof(StandaloneDocumentBatchEntity.GeneratedCount), "generated_count")]
    [InlineData(nameof(StandaloneDocumentBatchEntity.ErrorCount), "error_count")]
    [InlineData(nameof(StandaloneDocumentBatchEntity.ClaimedAt), "claimed_at")]
    [InlineData(nameof(StandaloneDocumentBatchEntity.CompletedAt), "completed_at")]
    public void LasColumnasCriticasDelLoteSeLlamanComoEnElDdl(string propiedad, string columna)
    {
        using var db = NewContext();

        db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(StandaloneDocumentBatchEntity))!
            .FindProperty(propiedad)!
            .GetColumnName()
            .Should().Be(columna);
    }

    [Theory]
    [InlineData(nameof(StandaloneDocumentEntity.BatchId), "batch_id")]
    [InlineData(nameof(StandaloneDocumentEntity.RowNumber), "row_number")]
    [InlineData(nameof(StandaloneDocumentEntity.ValidationErrors), "validation_errors")]
    public void ElVinculoConElLoteSeMapeaEnLaFilaDelDocumento(string propiedad, string columna)
    {
        using var db = NewContext();

        db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(StandaloneDocumentEntity))!
            .FindProperty(propiedad)!
            .GetColumnName()
            .Should().Be(columna);
    }

    [Fact]
    public void ValidationErrorsEsJsonbNoNuloConListaVaciaPorDefecto()
    {
        using var db = NewContext();

        var propiedad = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(StandaloneDocumentEntity))!
            .FindProperty(nameof(StandaloneDocumentEntity.ValidationErrors))!;

        propiedad.GetColumnType().Should().Be("jsonb");
        propiedad.IsNullable.Should().BeFalse();
    }

    private static string MigrationPath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir!.FullName, "services", "core-api", "src", "Flit.Infrastructure", "Migrations", file);
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);
}
