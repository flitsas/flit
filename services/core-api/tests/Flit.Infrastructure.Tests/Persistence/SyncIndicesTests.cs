using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13075 (Feature #13062) — índices de la sincronización externa.
/// <para>
/// Verifica que el DDL <i>diga</i> lo que debe; que el motor los use en las consultas del feed lo
/// cubre <c>SyncIndicesMigrationTests</c> en Flit.Integration.Tests, contra Postgres 16.
/// </para>
/// </summary>
public sealed class SyncIndicesTests
{
    private const string DdlResource = "Flit.Infrastructure.Persistence.Sql.Ddl.124-HU13075-sync-indices.sql";

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
    public void LaVersionTieneIndiceUnico() =>
        LoadDdl().Should().Contain(
            "CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_sync_version\n    ON tramites.procedure_instances (sync_version);");

    [Theory]
    [InlineData("ix_pi_status_history_radicado", "(procedure_instance_id)", "WHERE to_status IN ('preasignacion', 'entregado')")]
    [InlineData("ix_pi_status_history_aprobado", "(procedure_instance_id, changed_at DESC)", "WHERE to_status = 'aprobado'")]
    [InlineData("ix_pi_attachments_factura", "(procedure_instance_id, uploaded_at DESC)", "WHERE tipo = 'factura'")]
    public void LosIndicesDeApoyoEmpiezanPorElTramiteYSonParciales(string indice, string columnas, string filtro)
    {
        var ddl = LoadDdl();
        var desde = ddl.IndexOf($"CREATE INDEX IF NOT EXISTS {indice}", StringComparison.Ordinal);
        desde.Should().BeGreaterThanOrEqualTo(0, $"el DDL debe crear {indice}");

        var sentencia = ddl[desde..ddl.IndexOf(';', desde)];
        sentencia.Should().Contain(columnas).And.Contain(filtro);
    }

    [Fact]
    public void ElIndiceDeFacturaNoFiltraPorUnaColumnaQueNoExiste() =>
        LoadDdl().Should().NotContain("deleted_at");
}
