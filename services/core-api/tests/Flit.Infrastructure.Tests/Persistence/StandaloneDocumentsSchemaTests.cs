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
/// HU #12203 (Feature #12201, ADR-0056-generacion-documental-standalone) — esquema de
/// <c>admin.standalone_documents</c> y su trigger de inmutabilidad.
///
/// <para><b>Verificación estática del DDL embebido: en esta suite no hay Postgres</b> (el repo no
/// tiene Testcontainers ni fixture de base real), igual que en
/// <see cref="NotificationTestSettingsSchemaTests"/>. Se comprueba que el script dice lo que debe
/// decir. Las pruebas del modelo EF, en cambio, interrogan el modelo real y sí son de
/// comportamiento: son las que atrapan un nombre de columna equivocado, que de otro modo
/// reventaría en runtime y no al compilar.</para>
/// </summary>
public sealed class StandaloneDocumentsSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.105-F12201-generacion-documental.sql";

    private static string Load(string resource = DdlResource)
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>El script sin comentarios: lo que el motor realmente ejecuta.</summary>
    private static string Statements() => Regex.Replace(Load(), @"(?m)^\s*--.*$", string.Empty);

    /// <summary>Cuerpo de la función del trigger de inmutabilidad, sin comentarios.</summary>
    private static string TriggerBody()
    {
        var sql = Statements();
        var start = sql.IndexOf("CREATE OR REPLACE FUNCTION admin.trg_standalone_document_immutable()", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "el trigger de inmutabilidad debe existir");
        var end = sql.IndexOf("LANGUAGE plpgsql", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return sql[start..end];
    }

    // ── Generación sin trámite: ninguna FK hacia tramites.*, ningún BYTEA ─────────────

    [Fact]
    public void LaTablaNoReferenciaNingunObjetoDelSchemaDeTramites()
    {
        var sql = Statements();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.standalone_documents");
        sql.Should().NotContain("tramites.", "CF-02: el documento se emite SIN trámite y no puede colgar de uno");
        sql.Should().NotMatchRegex(@"(?i)procedure_instance");
    }

    [Fact]
    public void ElBinarioNoViveEnLaFila()
    {
        // Contraste deliberado con admin.impronta_generations, que sí usa BYTEA y NO se replica.
        Statements().Should().NotMatchRegex(@"(?i)\bbytea\b");
        Statements().Should().Contain("storage_path");
        Statements().Should().Contain("storage_sha256");
    }

    [Fact]
    public void ElDdlEsIdempotenteYSinTransaccionPropia()
    {
        var sql = Statements();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
        sql.Should().NotMatchRegex(@"CREATE TABLE (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"CREATE (UNIQUE )?INDEX (?!IF NOT EXISTS)");

        // BEGIN/COMMIT chocarían con la transacción de la migración EF que lo ejecuta. El BEGIN del
        // cuerpo plpgsql no cuenta: va precedido de "AS $$".
        sql.Should().NotMatchRegex(@"(?im)^\s*BEGIN\s*;");
        sql.Should().NotMatchRegex(@"(?im)^\s*COMMIT\s*;");
    }

    [Fact]
    public void LosNombresDeColumnaCriticosSonLosDelDdl()
    {
        var sql = Statements();

        sql.Should().MatchRegex(@"(?m)^\s*created_by_user_id\s+uuid\s+NOT NULL");
        sql.Should().MatchRegex(@"(?m)^\s*scenario\s+char\(1\)");
        sql.Should().MatchRegex(@"(?m)^\s*storage_sha256\s+varchar\(64\)");

        // Los nombres que existen en OTRAS tablas del schema y que aquí serían un error de mapeo.
        sql.Should().NotContain("flit_user_id");
        sql.Should().NotMatchRegex(@"(?m)^\s*sha256\s");
    }

    [Fact]
    public void LaIdempotenciaEsUnUnicoParcialPorTenant()
    {
        Statements().Should().MatchRegex(
            @"CREATE UNIQUE INDEX IF NOT EXISTS uq_standalone_documents_tenant_idempotency\s*\r?\n?\s*ON admin\.standalone_documents \(tenant_id, idempotency_key\)\s*\r?\n?\s*WHERE idempotency_key IS NOT NULL AND deleted_at IS NULL");
    }

    [Fact]
    public void UnaFilaEnErrorObligaCodigoYUnaGeneradaObligaBinario()
    {
        var sql = Statements();

        sql.Should().Contain("CONSTRAINT ck_standalone_documents_error_con_codigo");
        sql.Should().Contain("CONSTRAINT ck_standalone_documents_generated_completo");
        sql.Should().Contain("CONSTRAINT ck_standalone_documents_scenario_por_tipo");
        sql.Should().Contain("CHECK (status IN ('pending', 'processing', 'generated', 'error'))");
    }

    // ── CF-05 / CF-26 — lo que el trigger CONGELA ────────────────────────────────────

    [Fact]
    public void ElTriggerEsSoloBeforeUpdate()
    {
        var sql = Statements();

        sql.Should().MatchRegex(
            @"CREATE TRIGGER tr_standalone_documents_immutable\s*\r?\n?\s*BEFORE UPDATE ON admin\.standalone_documents");

        // Copiar el patrón de tramites.trg_field_value_immutable (INSERT OR UPDATE) dejaría CF-19
        // inservible: la auditoría de descarga tiene que poder escribir.
        sql.Should().NotContain("CREATE TRIGGER tr_standalone_documents_immutable\n  BEFORE INSERT");
        Regex.IsMatch(sql, @"tr_standalone_documents_immutable\s*\r?\n?\s*BEFORE INSERT")
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("status")]
    [InlineData("storage_path")]
    [InlineData("storage_sha256")]
    [InlineData("size_bytes")]
    [InlineData("filename")]
    [InlineData("input_summary")]
    [InlineData("scenario")]
    public void UnaFilaGeneradaCongelaLaColumna(string columna)
    {
        var body = TriggerBody();

        var capa3 = body[body.IndexOf("IF OLD.status = 'generated' THEN", StringComparison.Ordinal)..];
        capa3.Should().MatchRegex($@"NEW\.{columna}\s+IS DISTINCT FROM OLD\.{columna}",
            $"con OLD.status='generated' la columna {columna} no puede mutar (CF-05)");
        capa3.Should().Contain("ERRCODE = 'check_violation'");
    }

    [Fact]
    public void ElMensajeDeRechazoCitaElIdDeLaFila()
    {
        var body = TriggerBody();

        // Sin el id, un rechazo en un lote no dice QUÉ fila lo produjo.
        Regex.Matches(body, @"RAISE EXCEPTION '[^']*\(id=%\)'[^;]*, OLD\.id")
            .Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Theory]
    [InlineData("document_snapshot")]
    [InlineData("rues_snapshot")]
    public void ElSnapshotSeCongelaEnCualquierEstadoUnaVezEscrito(string columna)
    {
        var body = TriggerBody();

        body.Should().MatchRegex(
            $@"IF OLD\.{columna} IS NOT NULL\s*\r?\n?\s*AND NEW\.{columna} IS DISTINCT FROM OLD\.{columna} THEN",
            $"{columna} es la evidencia reproducible del documento");
    }

    [Fact]
    public void LaIdentidadDeLaFilaNuncaMuta()
    {
        var body = TriggerBody();

        foreach (var columna in new[] { "tenant_id", "created_by_user_id", "document_type", "created_at" })
        {
            body.Should().MatchRegex($@"NEW\.{columna}\s+IS DISTINCT FROM OLD\.{columna}");
        }
    }

    // ── CF-19 / R14 — lo que el trigger EXIME (si esto se rompe, la auditoría muere) ──

    [Theory]
    [InlineData("downloaded_at")]
    [InlineData("download_count")]
    [InlineData("row_version")]
    [InlineData("updated_at")]
    [InlineData("updated_by")]
    [InlineData("deleted_at")]
    [InlineData("deleted_by")]
    public void LaColumnaEximidaNoSeCompara(string columna)
    {
        var body = TriggerBody();

        // Si alguien la agrega a cualquiera de las tres capas, CF-19 (auditoría de descarga) o el
        // borrado lógico dejan de funcionar contra una fila ya generada.
        Regex.IsMatch(body, $@"NEW\.{columna}\s+IS DISTINCT FROM OLD\.{columna}")
            .Should().BeFalse($"{columna} debe poder actualizarse aun con status='generated' (CF-19 / R14)");
    }

    [Fact]
    public void LaAuditoriaDeDescargaTieneColumnasYCoherenciaPropias()
    {
        var sql = Statements();

        sql.Should().MatchRegex(@"(?m)^\s*downloaded_at\s+timestamptz");
        sql.Should().MatchRegex(@"(?m)^\s*download_count\s+integer\s+NOT NULL DEFAULT 0");
        sql.Should().Contain("CONSTRAINT ck_standalone_documents_download_coherente");
    }

    [Fact]
    public void ElTriggerDeInmutabilidadCorreAntesQueElDeRowVersion()
    {
        // PostgreSQL dispara los BEFORE por orden alfabético: ..._immutable < ..._row_version. Por
        // eso row_version aún no fue incrementado cuando corre la comparación y está exento.
        string.CompareOrdinal("tr_standalone_documents_immutable", "tr_standalone_documents_row_version")
            .Should().BeNegative();
    }

    // ── RLS: existe, pero NO es un control (§5.4 del diseño) ─────────────────────────

    [Fact]
    public void LaPoliticaRlsExisteYSeDocumentaComoDecorativa()
    {
        Statements().Should().Contain("CREATE POLICY tenant_isolation ON admin.standalone_documents");

        // Ni FORCE ROW LEVEL SECURITY ni pretensión de aislamiento: la app conecta como owner.
        Statements().Should().NotContain("FORCE ROW LEVEL SECURITY");
        Load().Should().Contain("NO ES UN CONTROL");
    }

    // ── CF-23 — la migración de I1 no arrastra el esquema de lotes de I3 ──────────────

    [Fact]
    public void LaMigracionDeI1SoloEjecutaElDdl105()
    {
        var migracion = File.ReadAllText(MigrationPath("20260909120000_F12201_GeneracionDocumental.cs"));

        migracion.Should().Contain("EmbeddedDdl.LoadUp(\"105-F12201-generacion-documental.sql\")");
        migracion.Should().NotContain("106-F12201-generacion-documental-lotes.sql");
        migracion.Should().Contain("DROP TABLE IF EXISTS admin.standalone_documents");
    }

    [Fact]
    public void ElDdlDeI1NoDeclaraNingunaColumnaDeLote()
    {
        var sql = Statements();

        // batch_id / row_number / validation_errors los agrega el DDL 106 en I3 (HU-07).
        sql.Should().NotContain("batch_id");
        sql.Should().NotContain("validation_errors");
        sql.Should().NotContain("standalone_document_batches");
    }

    [Fact]
    public void ElEsquemaDeLotesLlegaConHu07YAhoraSiTieneCodigoQueLoUsa()
    {
        // Este test era la guarda inversa en I1 —«ninguna entidad mapea la tabla de lotes»— y su
        // vuelta es justamente el criterio de aceptación de HU-07: el par de migración de I3 viaja
        // con esta HU y ya hay código que lo usa, así que la tabla DEBE estar en el modelo. Dejarlo
        // como estaba habría obligado a desplegar el esquema sin worker, o el worker sin esquema.
        using var db = NewContext();
        db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Should().Contain("standalone_document_batches",
                "desde HU-07 el worker de lotes consume la cabecera");
    }

    // ── Modelo EF — aquí sí se verifica comportamiento ────────────────────────────────

    [Fact]
    public void LaEntidadMapeaLaTablaDelSchemaAdminYQuedaFueraDeLasMigraciones()
    {
        using var db = NewContext();

        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(StandaloneDocumentEntity));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("standalone_documents");
        entity!.GetSchema().Should().Be("admin");
        entity!.IsTableExcludedFromMigrations().Should().BeTrue(
            "el DDL lo lleva el SQL crudo 105; si EF también lo generara habría dos fuentes del esquema");
    }

    [Theory]
    [InlineData(nameof(StandaloneDocumentEntity.CreatedByUserId), "created_by_user_id")]
    [InlineData(nameof(StandaloneDocumentEntity.Scenario), "scenario")]
    [InlineData(nameof(StandaloneDocumentEntity.StorageSha256), "storage_sha256")]
    [InlineData(nameof(StandaloneDocumentEntity.TenantId), "tenant_id")]
    [InlineData(nameof(StandaloneDocumentEntity.InputSummary), "input_summary")]
    [InlineData(nameof(StandaloneDocumentEntity.RuesSnapshot), "rues_snapshot")]
    [InlineData(nameof(StandaloneDocumentEntity.DocumentSnapshot), "document_snapshot")]
    [InlineData(nameof(StandaloneDocumentEntity.DownloadCount), "download_count")]
    [InlineData(nameof(StandaloneDocumentEntity.DownloadedAt), "downloaded_at")]
    public void LaPropiedadMapeaLaColumnaRealDelDdl(string propiedad, string columna)
    {
        using var db = NewContext();

        var property = db.Model.FindEntityType(typeof(StandaloneDocumentEntity))!.FindProperty(propiedad);

        property.Should().NotBeNull();
        property!.GetColumnName().Should().Be(columna);
    }

    [Fact]
    public void LosTresSnapshotsSonJsonbYNoTextoNiBinario()
    {
        using var db = NewContext();
        var entity = db.Model.FindEntityType(typeof(StandaloneDocumentEntity))!;

        foreach (var propiedad in new[] { "InputSummary", "RuesSnapshot", "DocumentSnapshot" })
        {
            entity.FindProperty(propiedad)!.GetColumnType().Should().Be("jsonb");
        }
    }

    [Fact]
    public void LaEntidadNoDeclaraNingunaPropiedadBinaria()
    {
        using var db = NewContext();

        db.Model.FindEntityType(typeof(StandaloneDocumentEntity))!
            .GetProperties()
            .Should().NotContain(p => p.ClrType == typeof(byte[]),
                "prohibido BYTEA: el PDF vive en storage");
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
