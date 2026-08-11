using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #11357 (Feature #11348) — esquema de la bitácora de envíos, interruptor propio de documentos
/// personalizados y los dos backfills.
/// <para>
/// Las pruebas leen el DDL embebido —igual que <see cref="GeneratedDocumentTypesSeedTests"/>— porque
/// el esquema lo lleva SQL crudo, no el scaffolding de EF. <b>Es verificación estática del script:
/// no hay Postgres en la suite</b> (el repo no tiene Testcontainers), así que estas pruebas
/// comprueban que el DDL dice lo que debe decir, no que el motor lo ejecute.
/// </para>
/// </summary>
public sealed class NotificationDeliveryLogSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.64-notification-delivery-log.sql";

    private const string BackfillInterruptorResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.65-backfill-personalized-documents-enabled.sql";

    private const string BackfillCanalResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.66-backfill-normalize-notification-channel.sql";

    private static string Load(string resourceName)
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"el DDL embebido {resourceName} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// El script sin comentarios: lo que el motor realmente ejecuta. Las aserciones de «esto NO
    /// aparece» se hacen sobre esto, porque los encabezados explican precisamente los términos que
    /// no deben ejecutarse.
    /// </summary>
    private static string Statements(string resourceName) =>
        Regex.Replace(Load(resourceName), @"(?m)^\s*--.*$", string.Empty);

    // ── AC1 — bitácora con aislamiento por tenant ────────────────────────────

    [Fact]
    public void AC1_CreaLaBitacoraConLasColumnasDelContrato()
    {
        var sql = Load(DdlResource);

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.notification_delivery_logs");
        sql.Should().MatchRegex(@"template_key\s+varchar\(\d+\)\s+NOT NULL");
        sql.Should().MatchRegex(@"channel\s+varchar\(\d+\)\s+NOT NULL");
        sql.Should().MatchRegex(@"recipient\s+varchar\(\d+\)\s+NOT NULL");
        sql.Should().MatchRegex(@"result\s+varchar\(\d+\)\s+NOT NULL");
        sql.Should().Contain("failure_reason");
        sql.Should().MatchRegex(@"duration_ms\s+integer\s+NOT NULL");
        sql.Should().MatchRegex(@"occurred_at\s+timestamptz\s+NOT NULL");
    }

    [Fact]
    public void AC1_IndexaPorTenantYFechaDescendente()
    {
        Load(DdlResource).Should().MatchRegex(
            @"CREATE INDEX IF NOT EXISTS ix_notification_delivery_logs_tenant_occurred_at\s*\r?\n?\s*ON admin\.notification_delivery_logs \(tenant_id, occurred_at DESC\)");
    }

    [Fact]
    public void AC1_HabilitaRlsConLaPoliticaDelEsquemaAdmin()
    {
        var sql = Load(DdlResource);

        sql.Should().Contain(
            "ALTER TABLE admin.notification_delivery_logs ENABLE ROW LEVEL SECURITY");
        sql.Should().Contain("CREATE POLICY tenant_isolation ON admin.notification_delivery_logs");
        sql.Should().Contain(
            "USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)");
    }

    [Fact]
    public void AC1_ElDestinatarioQuedaMarcadoComoDatoPersonalConSuFinalidad()
    {
        // Ley 1581: la columna no puede existir sin etiqueta ni sin finalidad declarada.
        var comentario = Regex.Match(
            Load(DdlResource),
            @"COMMENT ON COLUMN admin\.notification_delivery_logs\.recipient IS\s*'([^']*)'");

        comentario.Success.Should().BeTrue("recipient debe llevar COMMENT ON COLUMN");
        comentario.Groups[1].Value.Should().Contain("@pii:");
        comentario.Groups[1].Value.Should().Contain("auditoría de envíos");
    }

    [Fact]
    public void AC1_LaTablaLlevaComentario()
    {
        Load(DdlResource).Should().Contain("COMMENT ON TABLE admin.notification_delivery_logs IS");
    }

    // ── AC2 — interruptor propio ─────────────────────────────────────────────

    [Fact]
    public void AC2_AgregaElInterruptorBooleanoConDefaultFalso()
    {
        Load(DdlResource).Should().Contain(
            "ADD COLUMN IF NOT EXISTS personalized_documents_enabled boolean NOT NULL DEFAULT false");
    }

    [Fact]
    public void AC2_ElInterruptorRemiteALaDiscusionDelAdr()
    {
        // Nadie debe leer esta columna sin enterarse de que el ADR-0042 niega el booleano paralelo
        // y de que la resolución es el ADR-0043.
        var sql = Load(DdlResource);

        sql.Should().Contain("ADR-0042");
        sql.Should().Contain("ADR-0043");
    }

    // ── AC3 — el backfill preserva a los tenants con canal de API ────────────

    [Fact]
    public void AC3_ElBackfillVaEnSuPropioArchivo()
    {
        // El DDL no puede llevar el traslado dentro.
        Statements(DdlResource).Should().NotContain("UPDATE admin.tenant_operational_policies");
        Statements(BackfillInterruptorResource).Should()
            .Contain("UPDATE admin.tenant_operational_policies");
    }

    [Fact]
    public void AC3_EnciendeSoloATenantApiYNuncaApaga()
    {
        var sql = Statements(BackfillInterruptorResource);

        sql.Should().Contain("SET personalized_documents_enabled = true");
        sql.Should().Contain("WHERE notification_channel = 'tenant_api'");
        sql.Should().NotContain(
            "SET personalized_documents_enabled = false",
            "el backfill solo enciende: apagar dejaría a un tenant sin sus documentos");
        sql.Should().NotContain(
            "flit_smtp",
            "ningún tenant en flit_smtp puede quedar encendido por este script");
    }

    [Fact]
    public void AC3_ElBackfillEsIdempotente()
    {
        // Condición sobre el estado destino: re-ejecutarlo no toca filas.
        Load(BackfillInterruptorResource).Should()
            .Contain("AND personalized_documents_enabled = false");
    }

    // ── AC4 — idempotencia del DDL ───────────────────────────────────────────

    [Fact]
    public void AC4_TodoElDdlEsReaplicableSinError()
    {
        var sql = Statements(DdlResource);

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS");
        sql.Should().Contain("ADD COLUMN IF NOT EXISTS");
        sql.Should().Contain("CREATE INDEX IF NOT EXISTS");
        sql.Should().Contain("DROP POLICY IF EXISTS tenant_isolation");

        // Ninguna forma sin guarda que reviente en la segunda pasada.
        sql.Should().NotMatchRegex(@"CREATE TABLE (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"CREATE (UNIQUE )?INDEX (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"ADD COLUMN (?!IF NOT EXISTS)");
    }

    // ── AC5 — la bitácora no admite filas sin tenant ─────────────────────────

    [Fact]
    public void AC5_TenantIdEsNotNullYApuntaATenants()
    {
        var sql = Load(DdlResource);

        sql.Should().MatchRegex(@"tenant_id uuid NOT NULL");
        sql.Should().Contain("REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE");
        sql.Should().NotMatchRegex(
            @"tenant_id uuid NOT NULL DEFAULT",
            "un default enmascararía la inserción sin tenant en vez de rechazarla");
    }

    // ── Backfill 2 — normalización del canal (ampliación del encargo) ────────

    [Fact]
    public void Normalizacion_VaEnSuPropioArchivoYDejaTodoEnFlitSmtp()
    {
        var sql = Statements(BackfillCanalResource);

        sql.Should().Contain("SET notification_channel = 'flit_smtp'");
        sql.Should().Contain("WHERE notification_channel <> 'flit_smtp'");
        sql.Should().NotContain(
            "personalized_documents_enabled",
            "la normalización no debe tocar el interruptor: eso ya lo hizo el backfill anterior");
    }

    [Fact]
    public void Normalizacion_ExplicaElPorQueNoSoloElQue()
    {
        var sql = Load(BackfillCanalResource);

        sql.Should().Contain("#11311");
        sql.Should().Contain("#11362");
        sql.Should().Contain("#11359");
    }

    // ── Orden de ejecución ───────────────────────────────────────────────────

    [Fact]
    public void ElOrdenDeLosTresPasosLoGarantizaElTimestampDeLaMigracion()
    {
        // El número del archivo DDL no ordena nada: EF ejecuta por el id de la migración. Si alguien
        // renumera o reetiqueta, esta prueba cae antes que el despliegue.
        string IdDe(string typeName) =>
            typeof(FlitDbContext).Assembly
                .GetType($"Flit.Infrastructure.Migrations.{typeName}")!
                .GetCustomAttribute<MigrationAttribute>()!.Id;

        var esquema = IdDe("NotificationDeliveryLog");
        var interruptor = IdDe("BackfillPersonalizedDocumentsEnabled");
        var canal = IdDe("BackfillNormalizeNotificationChannel");

        esquema.Should().Match("*_NotificationDeliveryLog");
        string.CompareOrdinal(esquema, interruptor).Should().BeNegative(
            "la columna debe existir antes de que el backfill la escriba");
        string.CompareOrdinal(interruptor, canal).Should().BeNegative(
            "la elegibilidad debe trasladarse ANTES de borrar la señal (tenant_api) de la que se lee");
    }
}
