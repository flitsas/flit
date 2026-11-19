using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Correos 2.0 (guarda de copy): el rediseño del marco de los correos y del membrete de los PDF solo cambia el markup. El texto
/// visible de cada correo del conjunto FLIT queda fijado en <c>Golden/Copy/*.txt</c> y el HTML completo de los marcos de Renting
/// (que no se tocan) en <c>Golden/Copy/*.html</c>. Para regenerar a propósito: <c>GOLDEN_UPDATE=1</c>.
/// Con <c>COPY_PDF_DIR=/ruta</c> además se escriben los PDF de la línea base visual (DP, formato, carta y autorización).
/// </summary>
public sealed partial class CopyCongeladoTests
{
    private const string Recursos = "http://127.0.0.1:4023/assets/correo";
    private const string Empresa = "Gestora de Ejemplo S.A.S.";

    private static readonly (string, string?)[] Filas =
    [
        ("Empresa", Empresa), ("Paquete", "Lote octubre"), ("Archivo", "comparendo-001.pdf"), ("Error", "Timeout: sin respuesta en 30 s"), ("Motor", null),
    ];

    public static TheoryData<string> CorreosFlit() => new(Correos().Where(c => !c.Renting).Select(c => c.Nombre));

    public static TheoryData<string> MarcosRenting() => new(Correos().Where(c => c.Renting).Select(c => c.Nombre));

    [Theory]
    [MemberData(nameof(CorreosFlit))]
    public void El_texto_visible_de_los_correos_FLIT_no_cambia(string nombre) =>
        Comparar($"{nombre}.txt", TextoVisible(Correos().Single(c => c.Nombre == nombre).Html()));

    [Theory]
    [MemberData(nameof(MarcosRenting))]
    public void Los_marcos_de_Renting_no_cambian_ni_un_byte(string nombre) =>
        Comparar($"{nombre}.html", Correos().Single(c => c.Nombre == nombre).Html());

    [Fact]
    public void Linea_base_de_PDF_si_se_pide()
    {
        var dir = Environment.GetEnvironmentVariable("COPY_PDF_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(dir), "Solo con COPY_PDF_DIR (línea base visual de los PDF).");
        Directory.CreateDirectory(dir!);
        var valores = Valores();
        foreach (var (tipo, archivo) in new[]
                 {
                     (TipoPlantilla.DpDocumento, "DpDocumento.html"), (TipoPlantilla.DpDocumento, "Tesis.DA.html"), (TipoPlantilla.FormatoCambioContraventor, "FormatoCambioContraventor.html"),
                     (TipoPlantilla.CartaCambioContraventor, "CartaCambioContraventor.html"), (TipoPlantilla.AutorizacionCambioInfractor, "AutorizacionCambioInfractor.html"),
                 })
        {
            var armado = ServicioPlantillas.Armar(new PlantillaVersion { CuerpoHtml = Semilla(archivo).Cuerpo }, tipo, valores, ConjuntoPlantillas.Flit, Recursos);
            File.WriteAllBytes(Path.Combine(dir!, $"{Path.GetFileNameWithoutExtension(archivo)}.pdf"),
                ServicioPlantillas.Pdf(armado, titulo: archivo));
        }
    }

    // ── Los correos ─────────────────────────────────────────────────────────────────────────────────────────────────────
    private sealed record Correo(string Nombre, bool Renting, Func<string> Html);

    private static IEnumerable<Correo> Correos()
    {
        foreach (var tipo in new[]
                 {
                     TipoPlantilla.CorreoAviso1, TipoPlantilla.CorreoAviso2, TipoPlantilla.CorreoAviso3, TipoPlantilla.CorreoAviso4, TipoPlantilla.CorreoDpOt,
                     TipoPlantilla.CorreoCambioContraventorOt, TipoPlantilla.CorreoAlertaSinRl,
                 })
        {
            yield return new($"plantilla-{tipo}-flit", false, () => DePlantilla(tipo, ConjuntoPlantillas.Flit, $"{tipo}.html"));
            var renting = Existe($"Renting.{tipo}.html") ? $"Renting.{tipo}.html" : $"{tipo}.html";
            yield return new($"plantilla-{tipo}-renting", true, () => DePlantilla(tipo, ConjuntoPlantillas.Renting, renting));
        }

        // Los correos sin plantilla sembrada caen en Envolver (alertas y reportes armados con el editor).
        foreach (var tipo in new[] { TipoPlantilla.CorreoAlertaFallo, TipoPlantilla.CorreoReporteSemanal })
        {
            yield return new($"plantilla-{tipo}-flit", false, () => DeCuerpo(tipo, ConjuntoPlantillas.Flit));
            yield return new($"plantilla-{tipo}-renting", true, () => DeCuerpo(tipo, ConjuntoPlantillas.Renting));
        }

        var cuerpoTabla = $"<p>Falló la lectura del documento.</p>{HtmlCorreo.Tabla(Filas)}";
        yield return new("envolver-alerta-flit", false, () => HtmlCorreo.Envolver("Fallo del proveedor de OCR", cuerpoTabla));
        yield return new("envolver-pie-propio-flit", false, () => HtmlCorreo.Envolver("Fallo crítico de integración", cuerpoTabla, $"{Empresa} · Comunicación enviada con FLIT."));
        yield return new("envolver-renting", true, () => HtmlCorreo.Envolver(Empresa, cuerpoTabla, $"{Empresa} · Correo automático, no lo respondas.", marcaFlit: false));
        yield return new("deempresa-flit", false, () => HtmlCorreo.DeEmpresa(ConjuntoPlantillas.Flit, "Cierre semanal de comparendos", cuerpoTabla, Empresa, Recursos));
        yield return new("deempresa-sin-empresa-flit", false, () => HtmlCorreo.DeEmpresa(ConjuntoPlantillas.Flit, "Derechos de petición automáticos", cuerpoTabla, null, Recursos));
        yield return new("deempresa-renting", true, () => HtmlCorreo.DeEmpresa(ConjuntoPlantillas.Renting, "Cierre semanal de comparendos", cuerpoTabla, Empresa, Recursos));
        yield return new("flit-con-cierre", false, () => HtmlCorreo.Flit("<p>Cuerpo del aviso.</p>", Empresa, Recursos));
        yield return new("flit-sin-empresa", false, () => HtmlCorreo.Flit("<p>Cuerpo del aviso.</p>", null, Recursos));
        yield return new("renting-aviso", true, () => HtmlCorreo.Renting("<p>Cuerpo del aviso.</p>", Recursos));
        yield return new("renting-sistema", true, () => HtmlCorreo.RentingSistema("<p>Cuerpo.</p>", Empresa, Recursos));
        yield return new("tabla", false, () => HtmlCorreo.Tabla(Filas));
    }

    private static Dictionary<string, string?> Valores()
    {
        var valores = ContextoPlantilla.Ejemplo();
        valores["empresa.razon_social"] = Empresa;
        return valores;
    }

    private static string DePlantilla(TipoPlantilla tipo, ConjuntoPlantillas conjunto, string archivo)
    {
        var (asunto, cuerpo) = Semilla(archivo);
        return ServicioPlantillas.Armar(new PlantillaVersion { Asunto = asunto, CuerpoHtml = cuerpo }, tipo, Valores(), conjunto, Recursos).Html;
    }

    private static string DeCuerpo(TipoPlantilla tipo, ConjuntoPlantillas conjunto) =>
        ServicioPlantillas.Armar(new PlantillaVersion { Asunto = "Resumen {{empresa.razon_social}}", CuerpoHtml = $"<p>Hola {{{{empresa.razon_social}}}}.</p>{HtmlCorreo.Tabla(Filas)}" },
            tipo, Valores(), conjunto, Recursos).Html;

    // ── Semillas (embebidas en el ensamblado del API) ───────────────────────────────────────────────────────────────────
    [GeneratedRegex(@"<!--\s*asunto:\s*(.*?)\s*-->", RegexOptions.CultureInvariant)]
    private static partial Regex AsuntoRegex();

    [GeneratedRegex(@"<!--.*?-->\s*", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ComentarioRegex();

    private static bool Existe(string archivo) => typeof(ServicioPlantillas).Assembly.GetManifestResourceNames().Any(n => n.EndsWith($".Semillas.Plantillas.{archivo}", StringComparison.Ordinal));

    private static (string? Asunto, string Cuerpo) Semilla(string archivo)
    {
        var asm = typeof(ServicioPlantillas).Assembly;
        using var s = asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith($".Semillas.Plantillas.{archivo}", StringComparison.Ordinal)))!;
        var texto = new StreamReader(s).ReadToEnd();
        var asunto = AsuntoRegex().Match(texto) is { Success: true } m ? m.Groups[1].Value : null;
        return (asunto, ComentarioRegex().Replace(texto, string.Empty));
    }

    // ── Texto visible y golden ──────────────────────────────────────────────────────────────────────────────────────────
    [GeneratedRegex(@"<head.*?</head>|<style.*?</style>|<!--.*?-->", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvisibleRegex();

    [GeneratedRegex(@"<\s*(br|/p|/div|/h[1-6]|/tr|/li|/table|hr|/td|/th)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BloqueRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex EtiquetaRegex();

    [GeneratedRegex(@"[ \t ]+", RegexOptions.CultureInvariant)]
    private static partial Regex EspaciosRegex();

    /// <summary>El texto que lee la persona: sin etiquetas ni estilos, un bloque por línea y espacios normalizados.</summary>
    internal static string TextoVisible(string html)
    {
        var sin = InvisibleRegex().Replace(html, string.Empty);
        sin = BloqueRegex().Replace(sin, "\n");
        sin = WebUtility.HtmlDecode(EtiquetaRegex().Replace(sin, " "));
        var lineas = sin.Split('\n').Select(l => EspaciosRegex().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join('\n', lineas) + "\n";
    }

    private static void Comparar(string archivo, string actual, [CallerFilePath] string fuente = "")
    {
        var ruta = Path.Combine(Path.GetDirectoryName(fuente)!, "..", "Golden", "Copy", archivo);
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, actual, new UTF8Encoding(false));
            return;
        }

        File.Exists(ruta).Should().BeTrue($"falta la línea base {archivo} (GOLDEN_UPDATE=1 para crearla)");
        actual.Should().Be(File.ReadAllText(ruta), $"{archivo}: el copy está congelado; solo puede cambiar el markup");
    }
}
