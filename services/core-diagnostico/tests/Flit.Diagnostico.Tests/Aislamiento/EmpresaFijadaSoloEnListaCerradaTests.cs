using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #14576 AP-4 / SF-1 (D-81) — prueba de arquitectura (grep auditable, mismo escáner que
/// <see cref="IgnoreQueryFiltersSoloEnBackofficeTests"/>): un contexto cuyo filtro global es la empresa de OTRO y que escribe con
/// <c>SaveChanges</c> normal es un canal entre compañías que las pruebas de <c>IgnoreQueryFilters</c>, de escritura y de SQL crudo no
/// ven. Por eso sus tres puertas quedan en una lista cerrada de archivos de <c>services/core-diagnostico/src</c>:
/// <list type="number">
/// <item><c>AmbitoDeLaEmpresa.Abrir(</c>: solo la evidencia del saneamiento y la deuda del backoffice, después de validar la cadena
/// empresa → saneamiento.</item>
/// <item><c>.Fijar(</c> de <c>EmpresaDelTrabajo</c>: los trabajos en segundo plano actuales (consumidores, reintentos y reconciliadores)
/// y <c>AmbitoDeLaEmpresa</c>.</item>
/// <item><c>new DiagnosticoDb(</c> con un accessor propio: el trabajador del ZIP, el redactor en cola y la fábrica de diseño.</item>
/// </list>
/// Agregar un archivo a una lista exige revisar seguridad y anotarlo en ADR-0087 («Excepciones»). Comentarios, <c>bin/</c> y
/// <c>obj/</c> no cuentan.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EmpresaFijadaSoloEnListaCerradaTests
/// </code>
/// </example>
public sealed partial class EmpresaFijadaSoloEnListaCerradaTests
{
    private const string Api = "Flit.Diagnostico.Api/";

    /// <summary>Quién abre un ámbito fijado a la empresa explícita del backoffice (D-81).</summary>
    internal static readonly string[] LlamadoresDeAbrir =
    [
        Api + "Modulos/Backoffice/EvidenciaSaneamiento.cs",
        Api + "Modulos/Backoffice/SaneamientosBackofficeEndpoints.cs",
    ];

    /// <summary>Quién fija la empresa de un scope: los trabajos en segundo plano actuales y el ámbito del backoffice.</summary>
    internal static readonly string[] LlamadoresDeFijar =
    [
        Api + "Modulos/Backoffice/AmbitoDeLaEmpresa.cs",
        Api + "Modulos/Consultas/Bus/ConsumidorResultados.cs",
        Api + "Modulos/Consultas/Bus/ProgramadorReintentos.cs",
        Api + "Modulos/Derivacion/Bus/ConsumidorDerivacion.cs",
        Api + "Modulos/Derivacion/ReintentoDerivacion.cs",
        Api + "Modulos/Trabajos/ReconciliadorConsultas.cs",
        Api + "Modulos/Trabajos/ReconciliadorDerivacion.cs",
    ];

    /// <summary>Quién arma un <c>DiagnosticoDb</c> a mano con su propio accessor de empresa.</summary>
    internal static readonly string[] ConstructoresDelContexto =
    [
        Api + "Modulos/Reportes/EmpaquetadorZip.cs",
        Api + "Modulos/Trabajos/RedactorEnCola.cs",
        Api + "Persistence/DiagnosticoDb.cs",
    ];

    // AP-4 — AmbitoDeLaEmpresa.Abrir solo en la evidencia y la deuda del backoffice.
    [Fact]
    public void HU14576_AP4_AmbitoDeLaEmpresa_Abrir_solo_en_la_lista_cerrada() =>
        Verificar(Abrir(), LlamadoresDeAbrir, "AmbitoDeLaEmpresa.Abrir abre un contexto que lee y escribe en otra compañía");

    // AP-4 — EmpresaDelTrabajo.Fijar solo en los trabajos en segundo plano actuales y en AmbitoDeLaEmpresa.
    [Fact]
    public void HU14576_AP4_EmpresaDelTrabajo_Fijar_solo_en_la_lista_cerrada() =>
        Verificar(Fijar(), LlamadoresDeFijar, "Fijar cambia la empresa del filtro global del scope");

    // AP-4 — new DiagnosticoDb( (con un accessor propio) solo en el trabajador del ZIP, el redactor y la fábrica de diseño.
    [Fact]
    public void HU14576_AP4_new_DiagnosticoDb_solo_en_la_lista_cerrada() =>
        Verificar(NuevoContexto(), ConstructoresDelContexto, "un DiagnosticoDb armado a mano trae su propia empresa");

    // AP-4 — el escáner detecta un llamador fuera de la lista, acepta el de la lista y no cuenta comentarios ni obj/.
    [Fact]
    public void HU14576_AP4_el_escaner_detecta_un_llamador_fuera_de_la_lista()
    {
        var raiz = Directory.CreateTempSubdirectory("dgn-arq-").FullName;
        try
        {
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, Api + "Modulos/Backoffice/EvidenciaSaneamiento.cs", "await using var a = AmbitoDeLaEmpresa.Abrir(scopes, empresa);");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, Api + "Modulos/Dictamen/Otro.cs",
                "var a = AmbitoDeLaEmpresa\n    .Abrir (scopes, e);\nsp.GetRequiredService<EmpresaDelTrabajo>().Fijar(e);\nvar db = new DiagnosticoDb(opciones, acc);");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, Api + "Modulos/Comun/Doc.cs", "/// <see cref=\"AmbitoDeLaEmpresa.Abrir\"/>\n// x.Fijar(e); new DiagnosticoDb(o, a);");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, Api + "obj/Debug/Generado.cs", "AmbitoDeLaEmpresa.Abrir(s, e); x.Fijar(e);");

            Usos(raiz, Abrir()).Should().BeEquivalentTo([Api + "Modulos/Backoffice/EvidenciaSaneamiento.cs:1", Api + "Modulos/Dictamen/Otro.cs:2"]);
            Usos(raiz, Fijar()).Should().BeEquivalentTo([Api + "Modulos/Dictamen/Otro.cs:3"]);
            Usos(raiz, NuevoContexto()).Should().BeEquivalentTo([Api + "Modulos/Dictamen/Otro.cs:4"]);
            Fuera(Usos(raiz, Abrir()), LlamadoresDeAbrir).Should().BeEquivalentTo([Api + "Modulos/Dictamen/Otro.cs:2"]);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    private static void Verificar(Regex patron, string[] permitidos, string porque)
    {
        var usos = Usos(Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src"), patron);

        usos.Should().NotBeEmpty("si no hay usos, el escáner no mira donde debe");
        Fuera(usos, permitidos).Should().BeEmpty($"{porque}: solo en la lista cerrada (ADR-0087, SF-1)");
        permitidos.Should().OnlyContain(p => usos.Any(u => u.StartsWith(p + ":", StringComparison.Ordinal)),
            "la lista cerrada no guarda archivos que ya no llaman: si uno dejó de hacerlo, se quita de la lista");
    }

    private static List<string> Fuera(IEnumerable<string> usos, string[] permitidos) =>
        [.. usos.Where(u => !permitidos.Any(p => u.StartsWith(p + ":", StringComparison.Ordinal)))];

    /// <summary>
    /// Los usos de <paramref name="patron"/> como <c>archivo:línea</c>. Une cada línea con la siguiente para ver una llamada partida
    /// en dos líneas (<c>AmbitoDeLaEmpresa</c> y <c>.Abrir(</c>); el uso se cuenta en la línea donde termina el patrón.
    /// </summary>
    private static List<string> Usos(string raiz, Regex patron)
    {
        var usos = new List<string>();
        foreach (var archivo in Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(raiz, archivo).Replace('\\', '/');
            if (relativo.StartsWith("bin/", StringComparison.Ordinal) || relativo.StartsWith("obj/", StringComparison.Ordinal)
                || relativo.Contains("/bin/", StringComparison.Ordinal) || relativo.Contains("/obj/", StringComparison.Ordinal))
                continue;

            var lineas = File.ReadAllLines(archivo).Select(SinComentario).ToArray();
            var texto = string.Join('\n', lineas);
            foreach (Match m in patron.Matches(texto))
                usos.Add($"{relativo}:{texto[..(m.Index + m.Length)].Count(c => c == '\n') + 1}");
        }

        return usos;
    }

    private static string SinComentario(string linea)
    {
        var comentario = linea.IndexOf("//", StringComparison.Ordinal);
        return comentario >= 0 ? linea[..comentario] : linea;
    }

    [GeneratedRegex(@"\bAmbitoDeLaEmpresa\s*\.\s*Abrir\s*\(")]
    private static partial Regex Abrir();

    [GeneratedRegex(@"\.\s*Fijar\s*\(")]
    private static partial Regex Fijar();

    [GeneratedRegex(@"\bnew\s+DiagnosticoDb\s*\(")]
    private static partial Regex NuevoContexto();
}
