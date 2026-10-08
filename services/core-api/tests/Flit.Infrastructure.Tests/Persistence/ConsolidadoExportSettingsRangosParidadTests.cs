using System.Globalization;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13420 (AC3) — paridad entre los CHECK de <c>tramites.consolidado_export_settings</c> en el DDL 133 embebido y
/// <see cref="ConsolidadoExportSettingsRangos"/>, la única fuente de los rangos en C#. Si alguien cambia un CHECK (o una
/// constante) sin el otro, falla nombrando la restricción. Los extremos contra PostgreSQL real los cubre
/// <c>ParametrosMotorLoteIntegrationTests</c>.
/// <para>Uso de ejemplo: <c>ChecksDeUnaColumna()["ck_consolidado_export_settings_max_mb"]</c> → (max_mb_per_part, 10, 2048).</para>
/// </summary>
public sealed class ConsolidadoExportSettingsRangosParidadTests
{
    private const string Ddl = "133-HU13367-consolidado-export-batches.sql";

    /// <summary>Solo el bloque de la tabla de parámetros (hasta su <c>);</c>).</summary>
    private static string TablaSettings()
    {
        var sql = EmbeddedDdl.LoadUp(Ddl);
        var inicio = sql.IndexOf("CREATE TABLE IF NOT EXISTS tramites.consolidado_export_settings", StringComparison.Ordinal);
        inicio.Should().BeGreaterThanOrEqualTo(0);
        var fin = sql.IndexOf("\n);", inicio, StringComparison.Ordinal);
        return sql[inicio..fin];
    }

    /// <summary>CHECK de una columna: <c>BETWEEN a AND b</c>, <c>&gt; n</c> (mínimo n+1) o <c>&gt;= n</c>.</summary>
    private static Dictionary<string, (string Columna, int Minimo, int Maximo)> ChecksDeUnaColumna()
    {
        var checks = new Dictionary<string, (string, int, int)>();
        foreach (Match m in Regex.Matches(TablaSettings(),
                     @"CONSTRAINT (ck_\w+) CHECK \((\w+) (BETWEEN (\d+) AND (\d+)|>= (\d+)|> (\d+))\)"))
        {
            var columna = m.Groups[2].Value;
            int min, max = int.MaxValue;
            if (m.Groups[4].Success)
            {
                min = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                max = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            }
            else if (m.Groups[6].Success)
                min = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
            else
                min = int.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture) + 1;
            checks[m.Groups[1].Value] = (columna, min, max);
        }

        return checks;
    }

    [Fact]
    public void AC3_CadaCheckDeRangoDelDdl133_TieneSuRango_IdenticoEnRangos_YViceversa()
    {
        var ddl = ChecksDeUnaColumna();
        ddl.Should().HaveCount(10, "el DDL 133 acota diez columnas de la tabla de parámetros con un CHECK propio");

        var codigo = ConsolidadoExportSettingsRangos.Todos
            .ToDictionary(r => r.Restriccion, r => (r.Columna, r.Minimo, r.Maximo));

        codigo.Should().BeEquivalentTo(ddl, "los rangos del endpoint del Super Admin son los CHECK del DDL 133, sin otros");
    }

    [Fact]
    public void AC3_LasDosReglasLeaseMayorQueTimeout_ExistenEnElDdl_ConLasMismasColumnas()
    {
        var tabla = TablaSettings();
        foreach (var regla in ConsolidadoExportSettingsRangos.ReglasLease)
            tabla.Should().Contain(
                $"CONSTRAINT {regla.Restriccion} CHECK ({regla.ColumnaLease} > {regla.ColumnaTimeout})");

        Regex.Matches(tabla, @"CHECK \((\w+)_lease_seconds > (\w+)_timeout_seconds\)").Should().HaveCount(2);
    }

    [Fact]
    public void AC3_ElTopeTotal_UsaLaConstanteDeLaEntidad_1a32766()
    {
        var tope = ConsolidadoExportSettingsRangos.Todos.Single(r => r.Columna == "max_items_per_batch");

        tope.Maximo.Should().Be(ConsolidadoExportSettings.MaxItemsPerBatchMaximo).And.Be(32_766);
        tope.Minimo.Should().Be(1);
    }
}
