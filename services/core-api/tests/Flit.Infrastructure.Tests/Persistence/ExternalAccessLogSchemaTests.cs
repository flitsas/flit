using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13086 (Feature #13067) — el DDL de la bitácora de accesos externos dice lo que debe. El comportamiento
/// contra el motor lo cubre <c>ExternalAccessLogRepositoryTests</c> en Flit.Integration.Tests, contra Postgres 16.
/// </summary>
public sealed class ExternalAccessLogSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.127-HU13086-integrations-external-access-log.sql";

    private static string LoadDdl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(linea =>
        {
            var corte = linea.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? linea[..corte] : linea;
        }));
    }

    [Fact]
    public void LaBitacoraViveEnIntegrationsSinCompaniaNiRlsYEsIdempotente()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS integrations.external_access_log (");
        ddl.Should().NotContain("tenant_id uuid", "una página del feed toca varias compañías");
        ddl.Should().NotContain("ROW LEVEL SECURITY");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_external_access_log_client_occurred");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_external_access_log_occurred", "depuración por retención");
    }

    [Fact]
    public void AC1_TieneLasColumnasDeLaSolicitud()
    {
        var ddl = LoadDdl();

        foreach (var columna in new[]
                 {
                     "client_id         varchar(64)", "endpoint          varchar(80)  NOT NULL", "ip                inet",
                     "sync_version_from bigint", "sync_version_to   bigint", "items_count       integer",
                     "tenant_ids        uuid[]", "pii_unmasked      boolean      NOT NULL", "http_status       integer      NOT NULL",
                     "duration_ms       integer      NOT NULL", "occurred_at       timestamptz  NOT NULL",
                 })
        {
            ddl.Should().Contain(columna);
        }
    }

    [Fact]
    public void AC3_NoTieneDondeGuardarCuerposSecretosNiPases()
    {
        // Solo las columnas: los COMMENT ON sí nombran lo que NO se guarda.
        var ddl = LoadDdl();
        var inicio = ddl.IndexOf("CREATE TABLE IF NOT EXISTS integrations.external_access_log (", StringComparison.Ordinal);
        var columnas = ddl[inicio..ddl.IndexOf(");", inicio, StringComparison.Ordinal)].ToLowerInvariant();

        foreach (var prohibido in new[] { "body", "secret", "token", "payload", "authorization", "document_number", "email" })
        {
            columnas.Should().NotContain(prohibido);
        }
    }
}
