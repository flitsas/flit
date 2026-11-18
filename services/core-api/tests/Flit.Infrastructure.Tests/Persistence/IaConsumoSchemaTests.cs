using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk.IaConsumo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// ADR-0090 — el DDL 202 tiene EXACTAMENTE las columnas, tipos e índices del contrato ia-consumo v1
/// (docs/contratos/ia-consumo.v1.md, compartido con FLITO), el modelo EF que usa el escritor del SDK mapea las mismas
/// columnas y la migración carga el DDL embebido. Sin Postgres.
/// </summary>
public sealed partial class IaConsumoSchemaTests
{
    private const string DdlFile = "202-ia-consumo-log.sql";

    private static string LoadDdl()
    {
        const string resource = "Flit.Infrastructure.Persistence.Sql.Ddl." + DdlFile;
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return SinComentarios(reader.ReadToEnd());
    }

    private static string SinComentarios(string sql) => string.Join('\n', sql.Split('\n').Select(l =>
    {
        var corte = l.IndexOf("--", StringComparison.Ordinal);
        return corte >= 0 ? l[..corte] : l;
    }));

    /// <summary>El bloque ```sql del contrato, leído del repo (la fuente de verdad que comparten FLIT y FLITO).</summary>
    private static string ContratoDdl()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "contratos", "ia-consumo.v1.md")))
            dir = dir.Parent;
        dir.Should().NotBeNull("el contrato docs/contratos/ia-consumo.v1.md debe estar en el repo");
        var md = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "contratos", "ia-consumo.v1.md"));
        var inicio = md.IndexOf("```sql", StringComparison.Ordinal) + "```sql".Length;
        var fin = md.IndexOf("```", inicio, StringComparison.Ordinal);
        return SinComentarios(md[inicio..fin]);
    }

    /// <summary>(tabla, columna) → definición normalizada («tipo NULL/NOT NULL DEFAULT …»).</summary>
    private static Dictionary<string, string> Columnas(string ddl)
    {
        var columnas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match tabla in Tabla().Matches(ddl))
        {
            var nombre = tabla.Groups["tabla"].Value.Replace("admin.", string.Empty, StringComparison.Ordinal);
            foreach (var linea in tabla.Groups["cuerpo"].Value.Split('\n'))
            {
                var limpia = Espacios().Replace(linea.Trim().TrimEnd(','), " ").Replace("admin.", string.Empty, StringComparison.Ordinal);
                if (limpia.Length == 0 || limpia.StartsWith("UNIQUE", StringComparison.Ordinal))
                    continue;
                var espacio = limpia.IndexOf(' ', StringComparison.Ordinal);
                columnas[$"{nombre}.{limpia[..espacio]}"] = limpia[(espacio + 1)..];
            }
        }
        return columnas;
    }

    [GeneratedRegex(@"CREATE TABLE (IF NOT EXISTS )?(?<tabla>[\w.]+) \((?<cuerpo>.*?)\n\);", RegexOptions.Singleline)]
    private static partial Regex Tabla();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [Fact]
    public void ElDdlTieneExactamenteLasColumnasDelContrato()
    {
        var contrato = Columnas(ContratoDdl());
        var ddl = Columnas(LoadDdl());

        contrato.Should().HaveCount(56, "43 columnas de ia_consumo_log + 4 de ia_consumo_detalle + 9 de ia_tarifa_modelo");
        ddl.Should().BeEquivalentTo(contrato, "cambiar una columna en un solo lado rompe la consolidación con FLITO (contrato v2)");
    }

    [Fact]
    public void ElDdlTieneLosIndicesDelContratoYEsIdempotente()
    {
        var ddl = LoadDdl();
        foreach (Match indice in Regex.Matches(ContratoDdl(), @"CREATE INDEX (?<nombre>\w+)\s+ON (?<tabla>\w+) (?<resto>[^;]+);"))
        {
            var esperado = $"CREATE INDEX IF NOT EXISTS {indice.Groups["nombre"].Value}";
            ddl.Should().Contain(esperado);
            Espacios().Replace(ddl, " ").Should().Contain(
                $"{indice.Groups["nombre"].Value} ON admin.{indice.Groups["tabla"].Value} {Espacios().Replace(indice.Groups["resto"].Value, " ")};");
        }

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS admin.ia_consumo_log (");
        ddl.Should().Contain("ON CONFLICT (proveedor, modelo, vigente_desde) DO NOTHING");
        ddl.Should().NotContain("ROW LEVEL SECURITY", "tabla global de plataforma, como admin.ai_tool_provider_settings");
    }

    [Fact]
    public void LaSemillaSoloTraeTarifasDeAnthropic()
    {
        var ddl = LoadDdl();
        var semilla = ddl[ddl.IndexOf("INSERT INTO admin.ia_tarifa_modelo", StringComparison.Ordinal)..];
        var filas = Regex.Matches(semilla, @"\('(?<proveedor>\w+)', '(?<modelo>[\w.-]+)'");

        filas.Should().NotBeEmpty();
        filas.Select(f => f.Groups["proveedor"].Value).Should().OnlyContain(p => p == IaProveedores.Anthropic,
            "los proveedores sin precio confirmado no se siembran (costo NULL)");
        filas.Select(f => f.Groups["modelo"].Value).Should().Contain(["claude-haiku-4-5", "claude-sonnet-5"],
            "son los modelos por defecto del OCR y del clasificador");
    }

    [Fact]
    public void LaSemillaDelDdlEsLaMismaDelSdkQueUsanLosDemasServicios()
    {
        var ddl = Espacios().Replace(LoadDdl(), " ");
        var semilla = ddl[ddl.IndexOf("INSERT INTO admin.ia_tarifa_modelo", StringComparison.Ordinal)..];
        var filas = Regex.Matches(semilla, @"\('anthropic', '(?<modelo>[\w.-]+)', DATE '(?<fecha>[\d-]+)', (?<e>[\d.]+), (?<s>[\d.]+), (?<l>[\d.]+), (?<w>[\d.]+)\)")
            .Select(m => (m.Groups["modelo"].Value, m.Groups["fecha"].Value,
                decimal.Parse(m.Groups["e"].Value, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(m.Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(m.Groups["l"].Value, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(m.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        filas.Should().BeEquivalentTo(IaTarifasSemilla.Anthropic.Select(t =>
            (t.Modelo, IaTarifasSemilla.VigenteDesde.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
             t.Entrada, t.Salida, t.CacheLectura, t.CacheEscritura)));
        IaTarifasSemilla.Sql("admin").Should().Contain("ON CONFLICT (proveedor, modelo, vigente_desde) DO NOTHING");
    }

    [Fact]
    public void ElModeloEfMapeaLasMismasColumnasQueElDdlFueraDeLasMigraciones()
    {
        using var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(nameof(IaConsumoSchemaTests)).Options);
        var ddl = Columnas(LoadDdl());

        foreach (var (tipo, tabla) in new[]
                 {
                     (typeof(IaConsumoLog), "ia_consumo_log"),
                     (typeof(IaConsumoDetalle), "ia_consumo_detalle"),
                     (typeof(IaTarifaModelo), "ia_tarifa_modelo"),
                 })
        {
            var entidad = db.GetService<IDesignTimeModel>().Model.FindEntityType(tipo)!;
            entidad.GetSchema().Should().Be("admin");
            entidad.GetTableName().Should().Be(tabla);
            entidad.IsTableExcludedFromMigrations().Should().BeTrue("las crea el DDL 202, no EF");

            var store = StoreObjectIdentifier.Table(tabla, "admin");
            var mapeadas = entidad.GetProperties().Select(p => p.GetColumnName(store)!).ToList();
            mapeadas.Should().BeEquivalentTo(ddl.Keys.Where(k => k.StartsWith(tabla + ".", StringComparison.Ordinal))
                .Select(k => k[(tabla.Length + 1)..]));
        }
    }

    [Fact]
    public void LaMigracionCargaElDdlEmbebido()
    {
        var migracion = new IaConsumoTablas();
        var up = migracion.UpOperations.OfType<SqlOperation>().Single().Sql;
        up.Should().Contain("CREATE TABLE IF NOT EXISTS admin.ia_consumo_log");
        migracion.DownOperations.OfType<SqlOperation>().Single().Sql.Should().Contain("DROP TABLE IF EXISTS admin.ia_consumo_detalle");
        typeof(IaConsumoTablas).GetCustomAttributes(typeof(MigrationAttribute), false).Cast<MigrationAttribute>().Single().Id
            .Should().Be("20261118120000_IaConsumoTablas");
    }
}
