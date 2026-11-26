using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// Ninguna clase de pruebas arma un host de la API por prueba.
///
/// <para>xUnit crea una instancia de la clase por cada prueba: si el constructor, un campo o un método de la clase de
/// pruebas llama a <c>WithWebHostBuilder(…)</c> o hace <c>new WebApplicationFactory&lt;Program&gt;()</c>, cada prueba
/// arranca la API completa (~5 s de CPU, 30+ s con el runner cargado) y no la libera. Así pasó admin de 10 a 40 min,
/// dos veces en un mes (PR #760, #921).</para>
///
/// <para>Lo correcto es armar el host en un fixture de clase: una clase anidada que derive de <see cref="HostPorClase"/>
/// (o cualquier fixture propio, como <c>Hosts : IDisposable</c>) y <c>IClassFixture&lt;…&gt;</c>. El barrido es
/// léxico sobre el fuente de <c>Flit.Admin.Tests</c> y <c>Flit.Integration.Tests</c>: quita comentarios y literales,
/// sigue las llaves para saber en qué clase cae cada creación de host y marca la clase solo si ESA clase (no una
/// anidada) tiene <c>[Fact]</c> o <c>[Theory]</c>.</para>
///
/// <para>Hay dos listas: <see cref="Excepciones"/> (el host no se puede compartir, con su motivo) y
/// <see cref="Pendientes"/> (deuda conocida al crear esta prueba, a migrar). Las dos se vigilan en los dos sentidos:
/// una clase nueva que no esté en ellas falla, y una que ya no arme el host por prueba también falla para que se quite
/// de la lista.</para>
/// </summary>
public sealed class HostPorPruebaArchitectureTests
{
    /// <summary>Clase → motivo. Agregar aquí solo si el estado del host no se puede compartir entre pruebas.</summary>
    private static readonly Dictionary<string, string> Excepciones = new(StringComparer.Ordinal)
    {
        ["IdentityHostSurvivalTests"] = "prueba justamente el arranque del host con distintas configuraciones",
    };

    /// <summary>
    /// Deuda conocida (2026-11-19): clases que todavía arman el host por prueba. Migrarlas a <see cref="HostPorClase"/>
    /// (punto 2 del plan de CI) y quitarlas de aquí. No agregar clases nuevas a esta lista.
    /// </summary>
    private static readonly HashSet<string> Pendientes = new(StringComparer.Ordinal)
    {
        "AdminPlataformaNotificacionesCanalesEndpointsTests",
        "AdminPlataformaNotificacionesEnviosEndpointTests",
        "ConsolidadoLoteEndpointTests",
        "ConsultasConsumoEndpointTests",
        "DetailedReportCanalEndpointsTests",
        "IctServiceTokenTests",
        "JwtIssuedTokenValidationTests",
        "MandateSignerReassignRollbackTests",
        "MandatoGeneracionSinConvenioArchitectureTests",
        "OidcServerTests",
        "PersonaNaturalEstadoDeVueltaPostgresTests",
        "PersonaNaturalIdentidadAislamientoTests",
        "PersonaNaturalRadicacionPostgresTests",
        "PlatformEndpointsTests",
        "PublicBrandingEndpointsTests",
                "SessionPolicyEndpointTests",
        // En staging/release aún no migradas (en develop ya usan fixture Hosts / HostPorClase).
        "PermissionEnforcementStateTests",
        "OidcProductTokenTests",
        "TiposTramiteGrpcTests",
        "MensajesMuertosEndpointTests",
        "ActivarIdentidadManualEndpointTests",
        "RegenerarEnlaceManualEndpointTests",
    };

    private static readonly string[] ProyectosBarridos = ["Flit.Admin.Tests", "Flit.Integration.Tests"];

    private const string ComoMigrar =
        "cada prueba arranca la API completa. Crea una clase anidada `public sealed class Anfitrion : HostPorClase`, " +
        "mueve lo que hacía el WithWebHostBuilder a `Configurar(factory)`, usa `IClassFixture<MiClase.Anfitrion>` y " +
        "`anfitrion.Factory` en las pruebas (ver HostPorClase.cs). Si el host de verdad no se puede compartir, agrégala " +
        "a Excepciones con su motivo";

    [Fact]
    public void NingunaClaseDePruebasArmaUnHostPorPrueba()
    {
        var hallazgos = Barrer();
        hallazgos.Should().NotBeEmpty("el barrido debe encontrar al menos las excepciones conocidas");

        var nuevos = hallazgos
            .Where(h => !Excepciones.ContainsKey(h.Clase) && !Pendientes.Contains(h.Clase))
            .Select(h => h.ToString())
            .ToList();

        string.Join(Environment.NewLine, nuevos).Should().BeEmpty(ComoMigrar);
    }

    [Fact]
    public void LasListasNoTienenClasesQueYaNoArmanElHostPorPrueba()
    {
        var conHostPorPrueba = Barrer().Select(h => h.Clase).ToHashSet(StringComparer.Ordinal);

        string.Join(", ", Excepciones.Keys.Concat(Pendientes).Where(c => !conHostPorPrueba.Contains(c)))
            .Should().BeEmpty("ya no arman el host por prueba (o se renombraron): quítalas de Excepciones/Pendientes");
    }

    [Fact]
    public void ElDetectorMarcaElHostDeLaClaseDePruebasYNoElDelFixture()
    {
        const string fuente = """
            public sealed class MalTests
            {
                private readonly HttpClient _c;
                public MalTests(WebApplicationFactory<Program> f) =>
                    _c = f.WithWebHostBuilder(b => b.UseSetting("x", "{")).CreateClient();
                [Fact] public void A() { var s = $"{(true ? "}" : "{")}"; }
                [Fact] public void A2() { var c = '{'; }
            }

            public sealed class TambienMalTests
            {
                [Theory, InlineData(1)]
                public void B(int i) { using var f = new WebApplicationFactory<Program>(); }
                [Fact] public void B2() { }
            }

            public sealed class UnaSolaPruebaTests
            {
                [Fact] public void F() { using var f = new WebApplicationFactory<Program>(); }
            }

            public sealed class BienTests(BienTests.Anfitrion a) : IClassFixture<BienTests.Anfitrion>
            {
                // WithWebHostBuilder( en un comentario no cuenta
                [Fact] public void C() { _ = a.Factory; var t = "WithWebHostBuilder("; }

                public sealed class Anfitrion : HostPorClase
                {
                    protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> f) =>
                        f.WithWebHostBuilder(b => { });
                }
            }

            public sealed class FixtureSuelto : IDisposable
            {
                public WebApplicationFactory<Program> F { get; } = new WebApplicationFactory<Program>().WithWebHostBuilder(_ => { });
                public void Dispose() { }
            }

            public sealed class HelperEstaticoTests(HelperEstaticoTests.Anfitrion a) : IClassFixture<HelperEstaticoTests.Anfitrion>
            {
                public sealed class Anfitrion : HostPorClase
                {
                    protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> f) => Construir(f);
                }
                private static WebApplicationFactory<Program> Construir(WebApplicationFactory<Program> f) => f.WithWebHostBuilder(_ => { });
                [Fact] public void D() { using var l = new LoggerFactory(); }
            }

            public sealed class MiFabrica(int x) : WebApplicationFactory<Program>;

            public sealed class Envoltorio
            {
                public Envoltorio() => F = new MiFabrica(1).WithWebHostBuilder(_ => { });
                public WebApplicationFactory<Program> F { get; }
            }

            public sealed class EnvoltorioTests
            {
                [Fact] public void G() { var e = new Envoltorio(); }
                [Fact] public void G2() { }
            }

            public sealed class FabricaPropiaTests
            {
                [PostgresFact]
                public async Task E() { await using var f = new MiFabrica(1); }
                [PostgresTheory] public void E2() { }
            }
            """;

        var fabricas = HostPorPruebaScanner.Fabricas(fuente).ToHashSet();
        fabricas.UnionWith(HostPorPruebaScanner.Envoltorios(fuente, fabricas).ToList());
        var hallazgos = HostPorPruebaScanner.Scan("Sintetico.cs", fuente, fabricas);

        fabricas.Should().Contain(["MiFabrica", "Envoltorio", "FixtureSuelto"]).And.NotContain("LoggerFactory");
        hallazgos.Select(h => h.Clase).Distinct().Should().BeEquivalentTo(["MalTests", "TambienMalTests", "FabricaPropiaTests", "EnvoltorioTests"]);
        hallazgos.Single(h => h.Clase == "MalTests").Linea.Should().Be(5);
    }

    private static List<HostPorPruebaHallazgo> Barrer()
    {
        var tests = LocalizarCarpetaTests();
        var archivos = ProyectosBarridos
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(tests, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Ruta: Path.GetRelativePath(tests, f), Fuente: File.ReadAllText(f)))
            .ToList();

        // Primera pasada: las subclases propias de WebApplicationFactory (MarcaBlancaApiFactory, Factory, …) y luego los
        // envoltorios: clases sin pruebas que arman un host al construirse (AlmacenFactory, HostsPorClase…). Hacer `new`
        // de un envoltorio dentro de una prueba también arranca un host por prueba.
        var fabricas = archivos.SelectMany(a => HostPorPruebaScanner.Fabricas(a.Fuente)).ToHashSet(StringComparer.Ordinal);
        fabricas.Should().Contain("MarcaBlancaApiFactory", "la primera pasada debe encontrar las fábricas propias");
        fabricas.UnionWith(archivos.SelectMany(a => HostPorPruebaScanner.Envoltorios(a.Fuente, fabricas)).ToList());

        return archivos.SelectMany(a => HostPorPruebaScanner.Scan(a.Ruta, a.Fuente, fabricas)).ToList();
    }

    private static string LocalizarCarpetaTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(dir.FullName, "tests");
            if (File.Exists(Path.Combine(candidato, "Flit.Admin.Tests", "HostPorClase.cs")))
                return candidato;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró tests/Flit.Admin.Tests subiendo desde " + AppContext.BaseDirectory);
    }
}

/// <summary>Una creación de host dentro de una clase de pruebas: archivo, línea (1-based) y clase.</summary>
public sealed record HostPorPruebaHallazgo(string Archivo, int Linea, string Clase)
{
    public override string ToString() => $"{Archivo}:{Linea} ({Clase})";
}

/// <summary>
/// Barrido léxico de <see cref="HostPorPruebaArchitectureTests"/>. Una creación de host es <c>WithWebHostBuilder(</c> o
/// <c>new</c> de <c>WebApplicationFactory</c> o de una subclase propia. Cuenta solo si cae en el cuerpo de una clase con
/// dos pruebas o más (<c>[Fact]</c>, <c>[Theory]</c>, <c>[PostgresFact]</c>…) y fuera de un miembro <c>static</c>: un helper
/// estático suele ser lo que llama el fixture. Límite conocido: un helper estático que cada prueba llame sí arma un host
/// por prueba y no se detecta.
/// </summary>
public static partial class HostPorPruebaScanner
{
    [GeneratedRegex(@"\b(?:class|record)\s+(?<nombre>[A-Za-z_]\w*)(?:\s*<[^>{;]*>)?(?:\s*\([^){;]*\))?\s*:\s*(?:[\w.]+\.)?WebApplicationFactory\s*<")]
    private static partial Regex FabricaPropia();

    [GeneratedRegex(@"\b(?:class|record|struct|interface)\s+(?<nombre>[A-Za-z_]\w*)|\[\s*\w*(?:Fact|Theory)\w*\b|\bstatic\b|\bWithWebHostBuilder\s*\(|\bnew\s+(?:[\w.]+\.)?(?<fabrica>\w+)\s*[<(]|[{};]")]
    private static partial Regex Token();

    /// <summary>Nombres de las clases del fuente que derivan de <c>WebApplicationFactory&lt;…&gt;</c>.</summary>
    public static IEnumerable<string> Fabricas(string fuente) =>
        FabricaPropia().Matches(SoloCodigo(fuente)).Select(m => m.Groups["nombre"].Value);

    private sealed class Tipo(string nombre)
    {
        public string Nombre { get; } = nombre;
        public int Pruebas { get; set; }
        public bool MiembroEstatico { get; set; }
        public List<int> Hosts { get; } = [];
    }

    public static List<HostPorPruebaHallazgo> Scan(string archivo, string fuente, IReadOnlySet<string> fabricas) =>
        Analizar(fuente, fabricas)
            .Where(t => t.Pruebas >= 2 && t.Hosts.Count > 0) // con una sola prueba, un host por prueba = uno por clase
            .Select(t => new HostPorPruebaHallazgo(archivo, t.Hosts.Min(), t.Nombre))
            .ToList();

    /// <summary>Clases sin pruebas que crean un host fuera de un miembro <c>static</c> (fixtures, envoltorios).</summary>
    public static IEnumerable<string> Envoltorios(string fuente, IReadOnlySet<string> fabricas) =>
        Analizar(fuente, fabricas).Where(t => t.Pruebas == 0 && t.Hosts.Count > 0).Select(t => t.Nombre);

    private static List<Tipo> Analizar(string fuente, IReadOnlySet<string> fabricas)
    {
        var codigo = SoloCodigo(fuente);
        var lineas = Lineas(codigo);

        // Pila de llaves: cada entrada es el tipo que abrió esa llave, o null si es un bloque cualquiera.
        var pila = new Stack<Tipo?>();
        var tipos = new List<Tipo>();
        string? tipoPendiente = null;
        Tipo? EnCuerpoDeTipo() => pila.Count > 0 ? pila.Peek() : null; // directamente en el cuerpo, no en un método

        foreach (Match m in Token().Matches(codigo))
        {
            var t = m.Value;
            var actual = pila.FirstOrDefault(x => x is not null);
            if (m.Groups["nombre"].Success)
                tipoPendiente = m.Groups["nombre"].Value;
            else if (t == "{")
            {
                Tipo? nuevo = null;
                if (tipoPendiente is not null)
                {
                    nuevo = new Tipo(tipoPendiente);
                    tipos.Add(nuevo);
                    tipoPendiente = null;
                }

                pila.Push(nuevo);
            }
            else if (t == "}")
            {
                if (pila.Count > 0) pila.Pop();
                if (EnCuerpoDeTipo() is { } padre) padre.MiembroEstatico = false; // terminó un miembro o un tipo anidado
            }
            else if (t == ";")
            {
                tipoPendiente = null; // record Foo(int X); sin cuerpo
                if (EnCuerpoDeTipo() is { } tipo) tipo.MiembroEstatico = false;
            }
            else if (t == "static")
            {
                if (EnCuerpoDeTipo() is { } tipo) tipo.MiembroEstatico = true;
            }
            else if (t.StartsWith('['))
            {
                if (actual is not null) actual.Pruebas++;
            }
            else if (actual is not null && !actual.MiembroEstatico)
            {
                var fabrica = m.Groups["fabrica"];
                if (!fabrica.Success || fabrica.Value == "WebApplicationFactory" || fabricas.Contains(fabrica.Value))
                    actual.Hosts.Add(LineaDe(lineas, m.Index));
            }
        }

        return tipos;
    }

    private static int[] Lineas(string codigo)
    {
        var inicios = new List<int> { 0 };
        for (var i = 0; i < codigo.Length; i++)
            if (codigo[i] == '\n') inicios.Add(i + 1);
        return [.. inicios];
    }

    private static int LineaDe(int[] inicios, int indice)
    {
        var i = Array.BinarySearch(inicios, indice);
        return (i >= 0 ? i : ~i - 1) + 1;
    }

    /// <summary>El fuente con comentarios y literales (de texto y de carácter) cambiados por espacios; conserva los saltos de línea.</summary>
    internal static string SoloCodigo(string s)
    {
        var sb = new StringBuilder(s);
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                var fin = s.IndexOf('\n', i);
                i = Borrar(sb, i, fin < 0 ? s.Length : fin);
            }
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var fin = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = Borrar(sb, i, fin < 0 ? s.Length : fin + 2);
            }
            else if (s[i] == '\'')
                i = Borrar(sb, i, FinCaracter(s, i));
            else if (s[i] == '"' || ((s[i] == '$' || s[i] == '@') && InicioDeTexto(s, i)))
                i = Borrar(sb, i, FinTexto(s, i));
            else
                i++;
        }

        return sb.ToString();
    }

    private static int Borrar(StringBuilder sb, int desde, int hasta)
    {
        for (var k = desde; k < hasta; k++)
            if (sb[k] != '\n' && sb[k] != '\r') sb[k] = ' ';
        return hasta;
    }

    private static bool InicioDeTexto(string s, int i)
    {
        var k = i;
        while (k < s.Length && (s[k] == '$' || s[k] == '@')) k++;
        return k < s.Length && s[k] == '"' && (i == 0 || !char.IsLetterOrDigit(s[i - 1]) && s[i - 1] != '_');
    }

    private static int FinCaracter(string s, int i)
    {
        var k = i + 1;
        if (k < s.Length && s[k] == '\\') k += 2; else k++;
        while (k < s.Length && s[k] != '\'' && s[k] != '\n') k++;
        return Math.Min(k + 1, s.Length);
    }

    /// <summary>Fin (exclusivo) del literal de texto que empieza en <paramref name="i"/>: normal, literal (@), interpolado ($) o crudo (""").</summary>
    private static int FinTexto(string s, int i)
    {
        var k = i;
        var dolares = 0;
        var literal = false;
        while (s[k] == '$' || s[k] == '@')
        {
            if (s[k] == '$') dolares++; else literal = true;
            k++;
        }

        var comillas = 0;
        while (k + comillas < s.Length && s[k + comillas] == '"') comillas++;

        if (comillas >= 3) // texto crudo: termina con la misma cantidad de comillas
        {
            var cierre = new string('"', comillas);
            k += comillas;
            while (k < s.Length)
            {
                if (dolares > 0 && s[k] == '{' && CuentaIguales(s, k, '{') >= dolares)
                {
                    k = FinHueco(s, k + dolares);
                    continue;
                }

                if (string.CompareOrdinal(s, k, cierre, 0, comillas) == 0) return k + comillas;
                k++;
            }

            return s.Length;
        }

        k++; // la comilla que abre
        while (k < s.Length)
        {
            var c = s[k];
            if (!literal && c == '\\') { k += 2; continue; }
            if (c == '"')
            {
                if (literal && k + 1 < s.Length && s[k + 1] == '"') { k += 2; continue; }
                return k + 1;
            }

            if (dolares > 0 && c == '{')
            {
                if (k + 1 < s.Length && s[k + 1] == '{') { k += 2; continue; }
                k = FinHueco(s, k + 1);
                continue;
            }

            if (!literal && c == '\n') return k;
            k++;
        }

        return s.Length;
    }

    private static int CuentaIguales(string s, int k, char c)
    {
        var n = 0;
        while (k + n < s.Length && s[k + n] == c) n++;
        return n;
    }

    /// <summary>Salta el código de un hueco de interpolación (con sus propios textos y llaves) hasta su llave de cierre.</summary>
    private static int FinHueco(string s, int k)
    {
        var profundidad = 0;
        while (k < s.Length)
        {
            var c = s[k];
            if (c == '"' || ((c == '$' || c == '@') && InicioDeTexto(s, k))) { k = FinTexto(s, k); continue; }
            if (c == '\'') { k = FinCaracter(s, k); continue; }
            if (c == '{') profundidad++;
            else if (c == '}')
            {
                if (profundidad == 0) return k + 1;
                profundidad--;
            }

            k++;
        }

        return s.Length;
    }
}
