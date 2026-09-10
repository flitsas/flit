using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #11365 (Feature #11349) — esquema de fila única del banco de pruebas de notificaciones.
/// <para>
/// Las pruebas del DDL leen el recurso embebido —igual que <see cref="NotificationDeliveryLogSchemaTests"/>—
/// porque el esquema lo lleva SQL crudo. <b>Es verificación estática del script: no hay Postgres en
/// la suite</b> (el repo no tiene Testcontainers ni fixture de base real), así que comprueban que el
/// DDL dice lo que debe decir, no que el motor lo ejecute. Las de AC3, en cambio, sí interrogan el
/// modelo EF real y son verificación de comportamiento.
/// </para>
/// </summary>
public sealed class NotificationTestSettingsSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.67-HU11365-notification-test-settings.sql";

    private static string Load()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// El script sin comentarios: lo que el motor realmente ejecuta. Las aserciones de «esto NO
    /// aparece» van sobre esto, porque el encabezado explica precisamente los términos que no deben
    /// ejecutarse (tenant_id, RLS, xmin).
    /// </summary>
    private static string Statements() => Regex.Replace(Load(), @"(?m)^\s*--.*$", string.Empty);

    // ── AC1 — la migración crea la tabla y siembra la fila única ─────────────

    [Fact]
    public void AC1_CreaLaTablaDeConfiguracionConSusDosCampos()
    {
        var sql = Load();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.notification_test_settings");
        sql.Should().Contain("CONSTRAINT pk_notification_test_settings PRIMARY KEY (id)");
        sql.Should().MatchRegex(@"test_recipient_email\s+varchar\(320\)");
        sql.Should().MatchRegex(@"last_test_sent_at\s+timestamptz");
    }

    [Fact]
    public void AC1_LosDosCamposNacenNulos()
    {
        var sql = Statements();

        // Ni NOT NULL ni default: la fila sembrada no puede inventar un buzón ni fingir un envío.
        sql.Should().NotMatchRegex(@"test_recipient_email\s+varchar\(320\)\s+NOT NULL");
        sql.Should().NotMatchRegex(@"test_recipient_email\s+varchar\(320\)[^,]*DEFAULT");
        sql.Should().NotMatchRegex(@"last_test_sent_at\s+timestamptz\s+NOT NULL");
        sql.Should().NotMatchRegex(@"last_test_sent_at\s+timestamptz[^,]*DEFAULT");
    }

    [Fact]
    public void AC1_SiembraExactamenteUnaFilaConAmbosCamposEnNulo()
    {
        var sql = Statements();

        sql.Should().MatchRegex(
            @"INSERT INTO admin\.notification_test_settings \(test_recipient_email, last_test_sent_at\)\s*\r?\n?\s*SELECT NULL, NULL");
        // Un solo INSERT en todo el script: dos sembrarían dos filas.
        Regex.Matches(sql, @"INSERT INTO admin\.notification_test_settings").Should().HaveCount(1);
    }

    [Fact]
    public void AC1_LaMarcaDelUltimoEnvioSeDocumentaComoSoporteDelLimiteDeFrecuencia()
    {
        // El campo existe aquí pero lo escribe la #11368: sin esta remisión, quien lo lea creerá
        // que está muerto y lo borrará.
        var sql = Load();

        sql.Should().Contain("#11368");
        sql.Should().Contain(
            "COMMENT ON COLUMN admin.notification_test_settings.last_test_sent_at IS");
    }

    // ── AC2 — idempotencia, creación Y sembrado ──────────────────────────────

    [Fact]
    public void AC2_LaCreacionEsReaplicableSinError()
    {
        var sql = Statements();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS");
        sql.Should().Contain("DROP TRIGGER IF EXISTS tr_notification_test_settings_row_version");
        sql.Should().Contain("DROP TRIGGER IF EXISTS tr_notification_test_settings_audit");

        sql.Should().NotMatchRegex(@"CREATE TABLE (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"CREATE (UNIQUE )?INDEX (?!IF NOT EXISTS)");
    }

    [Fact]
    public void AC2_ElSembradoTambienEsIdempotenteYNoPisaLoYaConfigurado()
    {
        var sql = Statements();

        // Condición sobre el estado destino: en la segunda pasada el INSERT no inserta nada.
        sql.Should().Contain("WHERE NOT EXISTS (SELECT 1 FROM admin.notification_test_settings)");

        // Un UPSERT que sobreescribiera borraría el buzón que ya configuró el SuperAdmin.
        sql.Should().NotContain("ON CONFLICT");
        sql.Should().NotContain("UPDATE admin.notification_test_settings");
    }

    // ── AC3 — coherencia del modelo (verificación de comportamiento) ─────────

    [Fact]
    public void AC3_LaEntidadEstaExcluidaDeLasMigracionesAutomaticas()
    {
        using var db = NewContext();

        // El modelo en tiempo de ejecución está podado: «excluida de migraciones» solo vive en el
        // modelo de diseño, que es justo el que consultan las migraciones.
        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(NotificationTestSettingsRow));

        entity.Should().NotBeNull("la configuración EF debe registrar la entidad");
        entity!.IsTableExcludedFromMigrations().Should().BeTrue(
            "el DDL lo lleva el SQL crudo; si EF también lo generara, habría dos fuentes del esquema");
    }

    [Fact]
    public void AC3_ElCampoDeVersionDeFilaEsTokenDeConcurrencia()
    {
        using var db = NewContext();

        var propiedad = db.Model
            .FindEntityType(typeof(NotificationTestSettingsRow))!
            .FindProperty(nameof(NotificationTestSettingsRow.RowVersion));

        propiedad.Should().NotBeNull();
        propiedad!.IsConcurrencyToken.Should().BeTrue();
        propiedad.GetColumnName().Should().Be("row_version");
    }

    [Fact]
    public void AC3_LaVersionDeFilaLaLlevaElMotorYNoXmin()
    {
        var sql = Statements();

        sql.Should().MatchRegex(@"row_version\s+bigint\s+NOT NULL DEFAULT 0");
        sql.Should().Contain("FOR EACH ROW EXECUTE FUNCTION public.trg_row_version()");
        sql.Should().NotContain("xmin", "este repo no usa xmin como token de concurrencia");
    }

    [Fact]
    public void QuienCambieElBuzonQuedaEnElHistoricoDeAuditoria()
    {
        // updated_by solo recuerda al último; el buzón recibe correos reales y su cambio debe dejar
        // rastro en audit.audit_logs.
        Statements().Should().Contain("FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log()");
    }

    // ── AC4 — la BD rechaza una segunda fila ─────────────────────────────────

    [Fact]
    public void AC4_ElIndiceUnicoSobreExpresionConstanteImpideLaSegundaFila()
    {
        // ((true)) da a toda fila la misma llave ⇒ la segunda viola unicidad. Es el mismo truco de
        // uq_quipux_settings_singleton.
        Load().Should().MatchRegex(
            @"CREATE UNIQUE INDEX IF NOT EXISTS uq_notification_test_settings_singleton\s*\r?\n?\s*ON admin\.notification_test_settings \(\(true\)\)");
    }

    // ── Global de plataforma: sin tenant_id y sin RLS, a propósito ───────────

    [Fact]
    public void SinTenantIdYSinRlsPorqueElBuzonEsGlobalDePlataforma()
    {
        var sql = Statements();

        // «tenant_id» sí aparece dentro del COMMENT ON TABLE (explicando su ausencia); lo que no
        // puede existir es la columna ni la política.
        sql.Should().NotMatchRegex(@"(?m)^\s*tenant_id\s");
        sql.Should().NotContain("REFERENCES identity.tenants");
        sql.Should().NotContain("ROW LEVEL SECURITY");
        sql.Should().NotContain("CREATE POLICY");
    }

    [Fact]
    public void ElPorQueDeLaAusenciaDeRlsQuedaEscritoEnElEncabezado()
    {
        // Una tabla en `admin` sin RLS llama la atención: quien revise merece leer que es deliberado
        // y cuál es el precedente.
        var sql = Load();

        sql.Should().Contain("SIN tenant_id");
        sql.Should().Contain("quipux_settings");
        sql.Should().Contain("GLOBAL DE PLATAFORMA");
    }

    // ── Ley 1581 ─────────────────────────────────────────────────────────────

    [Fact]
    public void ElBuzonQuedaMarcadoComoDatoPersonalConSuFinalidad()
    {
        var comentario = Regex.Match(
            Load(),
            @"COMMENT ON COLUMN admin\.notification_test_settings\.test_recipient_email IS\s*'([^']*)'");

        comentario.Success.Should().BeTrue("test_recipient_email debe llevar COMMENT ON COLUMN");
        comentario.Groups[1].Value.Should().Contain("@pii:");
        comentario.Groups[1].Value.Should().Contain("Finalidad:");
    }

    [Fact]
    public void LaTablaLlevaComentario()
    {
        Load().Should().Contain("COMMENT ON TABLE admin.notification_test_settings IS");
    }

    /// <summary>
    /// Contexto con el proveedor relacional: construir el modelo NO abre conexión, y solo el modelo
    /// relacional conoce «tabla excluida de migraciones» (InMemory descarta ese mapeo y la aserción
    /// pasaría por la razón equivocada).
    /// </summary>
    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);
}
