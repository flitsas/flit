using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13083 — DDL 128: el índice único de <c>sync_version</c> es parcial para que el sello de sincronización no
/// tome el bloqueo de clave (FOR UPDATE) y no cause deadlocks con las FK de las tablas hijas. El comportamiento
/// contra el motor lo cubre <c>ProcedureSyncConcurrencyTests</c> en Flit.Integration.Tests.
/// </summary>
public sealed class SyncVersionIndiceParcialTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.128-HU13083-sync-version-indice-sin-bloqueo-de-clave.sql";

    [Fact]
    public void ElIndiceUnicoDeSyncVersionEsParcialYConservaElNombre()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain(
            "CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_sync_version\n"
            + "    ON tramites.procedure_instances (sync_version)\n"
            + "    WHERE sync_version IS NOT NULL;");
        ddl.Should().Contain("i.indpred IS NULL", "solo reemplaza el índice si todavía es el corriente (idempotente)");
    }

    private static string LoadDdl()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(linea =>
        {
            var corte = linea.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? linea[..corte] : linea;
        }));
    }
}
