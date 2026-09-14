using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #12359 (Feature #12257, épica #12235) — AC3: en los repositorios analíticos de SQL directo
/// (<c>Persistence/Repositories/Analytics*.cs</c> y <c>DetailedReport*.cs</c>, incluido el reporte de red de
/// la HU #12360) los identificadores de
/// cliente viajan SIEMPRE como parámetro (<c>@tenant</c>, <c>@tenants</c> tipado <c>uuid[]</c>) y nunca
/// interpolados ni concatenados en el texto de la consulta. El barrido es léxico sobre el fuente y falla
/// nombrando <c>archivo:línea</c>:
/// <list type="bullet">
///   <item>Un hueco de interpolación (<c>$"…{x}…"</c>, <c>$@"…"</c>, <c>$"""…"""</c>) cuyo contenido NO sea
///   un fragmento constante del propio archivo (<c>const string</c> como <c>CategoriaCase</c> o
///   <c>ChangedInRange</c>): <c>{tenantId}</c>, <c>{string.Join(",", ids)}</c>, <c>{scope.X}</c>…</item>
///   <item><c>string.Format</c> / <c>String.Format</c> / <c>string.Concat</c> / <c>string.Join</c> con
///   un operando de tenant o de ids.</item>
///   <item>Concatenación <c>+</c> con un operando cuyo nombre contenga <c>tenant</c> o termine en
///   <c>Id</c>/<c>Ids</c>, y <c>.Replace("@tenant…"</c>.</item>
/// </list>
/// Lista blanca explícita: el <c>set_config</c> del GUC RLS pasa por
/// <c>ExecuteSqlInterpolatedAsync</c> (EF convierte cada hueco en parámetro; nunca concatena).
/// <para>Uso de ejemplo: no se invoca; corre en CI. <see cref="SqlInterpolationScanner.Scan"/> también se
/// ejercita con un fuente sintético para demostrar que el detector no está en verde por vacuidad.</para>
/// </summary>
public sealed class SqlInterpolationArchitectureTests
{
    private static readonly string[] ScannedFilePatterns = ["Analytics*.cs", "DetailedReport*.cs"];

    // ── AC3 — los repositorios reales están limpios ─────────────────────────────────────────

    [Fact]
    public void AC3_NingunRepositorioAnaliticoInterpolaNiConcatenaIdentificadoresEnSql()
    {
        var files = ScannedFiles();
        files.Should().NotBeEmpty("el barrido debe encontrar los repositorios analíticos");
        files.Select(Path.GetFileName).Should().Contain(["AnalyticsReadRepository.cs", "AnalyticsNetworkReadRepository.cs", "AnalyticsMetricsReadRepository.cs", "DetailedReportReadRepository.cs", "DetailedReportNetworkReadRepository.cs"]);

        var findings = files
            .SelectMany(f => SqlInterpolationScanner.Scan(Path.GetFileName(f), File.ReadAllLines(f)))
            .Select(f => f.ToString())
            .ToList();

        findings.Should().BeEmpty(
            "los identificadores de cliente viajan como parámetro (@tenant / @tenants uuid[]), nunca en el texto SQL (HU #12359 AC3)");
    }

    [Fact]
    public void AC3_LasConsultasDeRedRecibenElConjuntoComoParametroDeArreglo()
    {
        var network = ScannedFiles().Single(f => Path.GetFileName(f) == "AnalyticsNetworkReadRepository.cs");
        // Solo código: los comentarios XML de la clase también citan la expresión.
        var source = string.Join('\n', File.ReadAllLines(network).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var networkSqlCount = Regex.Count(source, @"private const string \w+NetworkSql\s*=");
        var anyTenantsCount = Regex.Count(source, @"tenant_id = ANY\(@tenants\)");

        networkSqlCount.Should().Be(3, "overview, top de productividad y tendencia mensual");
        anyTenantsCount.Should().Be(networkSqlCount, "cada consulta de red filtra con = ANY(@tenants)");
        source.Should().Contain("NpgsqlDbType.Array | NpgsqlDbType.Uuid", "el parámetro se tipa explícitamente como uuid[]");
        source.Should().NotContain("set_config(", "la rama de red no fija GUC por tenant: no hay un solo tenant");
    }

    /// <summary>
    /// HU #12360 AC3 — el repositorio del reporte de red comparte UN predicado (<c>BaseFrom</c>) con
    /// <c>= ANY(@tenants)</c> tipado <c>uuid[]</c>; todas sus consultas (<c>…NetworkSql</c>) se componen
    /// solo de fragmentos constantes y no fija GUC por tenant.
    /// </summary>
    [Fact]
    public void AC3_ElReporteDeRedRecibeElConjuntoComoParametroDeArregloEnUnSoloPredicado()
    {
        var network = ScannedFiles().Single(f => Path.GetFileName(f) == "DetailedReportNetworkReadRepository.cs");
        var source = string.Join('\n', File.ReadAllLines(network).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var networkSqlCount = Regex.Count(source, @"private const string \w+NetworkSql\s*=");
        var anyTenantsCount = Regex.Count(source, @"tenant_id = ANY\(@tenants\)");

        networkSqlCount.Should().Be(7, "página, exportación, conteo y cuatro desgloses (estado, categoría, tipo, cliente)");
        anyTenantsCount.Should().Be(1, "un único predicado BaseFrom compartido por listado y exportación (AC5)");
        source.Should().Contain("private const string BaseFrom", "el predicado es un fragmento constante");
        source.Should().Contain("NpgsqlDbType.Array | NpgsqlDbType.Uuid", "el parámetro se tipa explícitamente como uuid[]");
        source.Should().NotContain("set_config(", "la rama de red no fija GUC por tenant: no hay un solo tenant");
        Regex.IsMatch(source, @"= @tenant\b").Should().BeFalse("la rama de red no usa el parámetro escalar de la ruta de siempre");
    }

    // ── El detector detecta (caso sintético en memoria) ────────────────────────────────────

    [Fact]
    public void ElDetectorFallaNombrandoArchivoYLineaAnteUnaInterpolacionSintetica()
    {
        string[] source =
        [
            "internal sealed class FakeRepo",                                                              // 1
            "{",                                                                                           // 2
            "    private const string Fragment = \"pi.status\";",                                          // 3
            "    private const string OkSql = $\"\"\"SELECT {Fragment} FROM t WHERE tenant_id = @tenant\"\"\";", // 4 (ok)
            "    private static string Bad1(Guid tenantId) => $\"SELECT 1 FROM t WHERE tenant_id = '{tenantId}'\";", // 5
            "    private static string Bad2(IEnumerable<Guid> ids) => \"SELECT 1 WHERE tenant_id IN (\" + string.Join(\",\", ids) + \")\";", // 6
            "    private static string Bad3(Guid tenantId) => string.Format(\"WHERE tenant_id = '{0}'\", tenantId);", // 7
            "    private static string Bad4(Guid tenantId) => \"WHERE tenant_id = '\" + tenantId + \"'\";", // 8
            "    private static string Bad5(TenantScope scope) => $@\"WHERE tenant_id = ANY('{{{string.Join(\",\", scope.ReadTenantIds)}}}')\";", // 9
            "    private static string Bad6(string sql, Guid tenant) => sql.Replace(\"@tenant\", tenant.ToString());", // 10
            "}",                                                                                           // 11
        ];

        var findings = SqlInterpolationScanner.Scan("FakeRepo.cs", source);

        findings.Select(f => f.Line).Should().BeEquivalentTo([5, 6, 7, 8, 9, 10], "la línea 4 interpola un fragmento constante y es legítima");
        findings.Should().OnlyContain(f => f.File == "FakeRepo.cs");
        findings.Select(f => f.ToString()).Should().AllSatisfy(s => s.Should().StartWith("FakeRepo.cs:"));
    }

    [Fact]
    public void ElDetectorAdmiteSoloElSetConfigParametrizadoPorEf()
    {
        string[] allowed =
        [
            "await _context.Database.ExecuteSqlInterpolatedAsync(",
            "    $\"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)\", ct)",
        ];
        string[] notAllowed =
        [
            "await conn.ExecuteAsync(",
            "    $\"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)\")",
        ];
        string[] wrongStatement =
        [
            "await _context.Database.ExecuteSqlInterpolatedAsync(",
            "    $\"DELETE FROM tramites.procedure_instances WHERE tenant_id = {tenantId}\", ct)",
        ];

        SqlInterpolationScanner.Scan("A.cs", allowed).Should().BeEmpty();
        SqlInterpolationScanner.Scan("B.cs", notAllowed).Should().ContainSingle().Which.Line.Should().Be(2);
        SqlInterpolationScanner.Scan("C.cs", wrongStatement).Should().ContainSingle().Which.Line.Should().Be(2);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private static List<string> ScannedFiles()
    {
        var dir = Path.Combine(LocateCoreApiSourceDirectory(), "Flit.Infrastructure", "Persistence", "Repositories");
        return ScannedFilePatterns
            .SelectMany(p => Directory.EnumerateFiles(dir, p, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static string LocateCoreApiSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (File.Exists(Path.Combine(candidate, "Flit.Infrastructure", "Flit.Infrastructure.csproj")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró src/Flit.Infrastructure subiendo desde " + AppContext.BaseDirectory);
    }
}

/// <summary>Un hallazgo del barrido: archivo, línea (1-based), regla y el texto de la línea.</summary>
public sealed record SqlInterpolationFinding(string File, int Line, string Rule, string Text)
{
    public override string ToString() => $"{File}:{Line} [{Rule}] {Text.Trim()}";
}

/// <summary>
/// Detector léxico de interpolación / concatenación de identificadores en SQL directo (HU #12359 AC3).
/// Puro (líneas de texto → hallazgos) para poder demostrar con un fuente sintético que detecta.
/// </summary>
public static class SqlInterpolationScanner
{
    private static readonly Regex ConstFragment = new(@"\bconst\s+string\s+(\w+)\s*=", RegexOptions.Compiled);
    private static readonly Regex InterpolatedStart = new(@"(\$@|@\$|\$)""", RegexOptions.Compiled);
    private static readonly Regex Hole = new(@"(?<!\{)\{(?!\{)([^{}]+)\}(?!\})", RegexOptions.Compiled);
    private static readonly Regex FormatOrConcat = new(@"\b[Ss]tring\.(Format|Concat|Join)\s*\(", RegexOptions.Compiled);
    private static readonly Regex TenantConcat = new(
        @"(\+\s*(\w*[Tt]enant\w*|\w+Ids?)\b(?!\s*\()|\b(\w*[Tt]enant\w*|\w+Ids?)\b\s*\+)", RegexOptions.Compiled);
    private static readonly Regex ReplaceParam = new(@"\.Replace\(\s*""@", RegexOptions.Compiled);
    private static readonly Regex AllowedSetConfig = new(
        @"^\s*\$""SELECT set_config\('app\.current_tenant_id', \{tenantId\.ToString\(\)\}, true\)""", RegexOptions.Compiled);

    public static IReadOnlyList<SqlInterpolationFinding> Scan(string file, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(lines);

        var constants = new HashSet<string>(
            lines.SelectMany(l => ConstFragment.Matches(l).Select(m => m.Groups[1].Value)),
            StringComparer.Ordinal);

        var findings = new List<SqlInterpolationFinding>();
        var inInterpolated = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var number = i + 1;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal))
                continue;

            if (IsAllowedSetConfig(lines, i))
                continue;

            // Huecos de interpolación: dentro de una cadena interpolada (misma línea o raw multilínea).
            if (InterpolatedStart.IsMatch(line))
                inInterpolated = true;
            if (inInterpolated)
            {
                foreach (Match hole in Hole.Matches(line))
                {
                    var content = hole.Groups[1].Value.Trim();
                    if (!constants.Contains(content))
                        findings.Add(new SqlInterpolationFinding(file, number, "interpolación de identificador", line));
                }

                if (line.Contains("\"\"\";", StringComparison.Ordinal) || line.TrimEnd().EndsWith("\";", StringComparison.Ordinal)
                    || line.TrimEnd().EndsWith("\")", StringComparison.Ordinal) || line.TrimEnd().EndsWith("\",", StringComparison.Ordinal))
                    inInterpolated = false;
            }

            if (FormatOrConcat.IsMatch(line))
                findings.Add(new SqlInterpolationFinding(file, number, "string.Format/Concat/Join", line));
            else if (TenantConcat.IsMatch(line))
                findings.Add(new SqlInterpolationFinding(file, number, "concatenación con tenant/ids", line));

            if (ReplaceParam.IsMatch(line))
                findings.Add(new SqlInterpolationFinding(file, number, "Replace de parámetro", line));
        }

        return findings
            .GroupBy(f => (f.Line, f.Rule))
            .Select(g => g.First())
            .OrderBy(f => f.Line)
            .ToList();
    }

    /// <summary>
    /// Lista blanca: el <c>set_config</c> del GUC RLS SOLO cuando la línea anterior lo entrega a
    /// <c>ExecuteSqlInterpolatedAsync</c> (EF parametriza cada hueco de la <c>FormattableString</c>).
    /// </summary>
    private static bool IsAllowedSetConfig(IReadOnlyList<string> lines, int index)
    {
        if (!AllowedSetConfig.IsMatch(lines[index]))
            return false;

        return index > 0 && lines[index - 1].Contains("ExecuteSqlInterpolatedAsync(", StringComparison.Ordinal);
    }
}
