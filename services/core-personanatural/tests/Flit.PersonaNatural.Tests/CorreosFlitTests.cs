using System.Net;
using System.Text.RegularExpressions;
using Flit.PersonaNatural.Api.CorreoFlit;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Pagos;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Correos 2.0 — guarda del copy de los 3 correos del portal (cotización, cierre y anulación) al pasar al marco FLIT. El texto
/// visible es IDÉNTICO a la línea base tomada ANTES del cambio (2026-11-19): el marco no agrega texto (el pie es solo logo y
/// divisor). Cualquier diferencia de copy hace fallar la prueba.
/// </summary>
public sealed class CorreosFlitTests
{
    private const string Organismo = "Secretaría de Movilidad de Medellín";

    [Fact]
    public void Cotizacion_conserva_el_copy_y_lleva_las_lineas_en_la_tarjeta()
    {
        var html = CotizacionesService.ArmarHtml(new CotizacionDto(
        [
            new LineaCobroDto("A", "Derechos del organismo", "Traspaso particular", 250_000),
            new LineaCobroDto("B", "Servicio FLIT", "", 45_900),
            new LineaCobroDto("C", "Estampilla <pro> & más", "Departamental", 1_234_567),
        ], 1_530_467, "v1", []));

        Texto(html).Should().Be(
            "Tu cotización Derechos del organismo Traspaso particular $ 250.000 COP Servicio FLIT $ 45.900 COP "
            + "Estampilla <pro> & más Departamental $ 1.234.567 COP Total $ 1.530.467 COP "
            + "Valores vigentes al momento de enviar este correo; pueden cambiar. No guardamos tu correo.");
    }

    [Theory]
    [InlineData("María José Pérez", "Hola María José Pérez,")]
    [InlineData(null, "Hola,")]
    public void Cierre_conserva_el_copy(string? nombre, string saludo) =>
        Texto(CorreoDeCierre.ArmarHtml(nombre, "PN-7K3QX9", Organismo)).Should().Be(
            $"Tu trámite quedó pagado {saludo} Recibimos el pago de tu trámite. Tu código para consultarlo es PN-7K3QX9 . "
            + $"Recógelo en {Organismo} . Guarda este código: con él puedes consultar el estado de tu trámite.");

    [Theory]
    [InlineData("María José Pérez", "Hola María José Pérez,")]
    [InlineData("  ", "Hola,")]
    public void Anulacion_conserva_el_copy(string? nombre, string saludo) =>
        Texto(CorreoDeAnulacion.ArmarHtml(nombre, "PN-7K3QX9", Organismo)).Should().Be(
            $"Tu pago fue anulado {saludo} El pago en caja de tu trámite PN-7K3QX9 fue anulado por el organismo. "
            + $"Acércate a la caja de {Organismo} para generar el pago de nuevo. "
            + "Guarda este código: con él puedes consultar el estado de tu trámite.");

    [Fact]
    public void El_nombre_va_en_su_propia_linea_en_negrita_azul_y_sin_nombre_queda_igual()
    {
        CorreoDeCierre.ArmarHtml("Ana <b>", "PN-1", "Org")
            .Should().Contain("Hola<br><strong style=\"color:#557EFF;\">Ana &lt;b&gt;,</strong>");
        CorreoDeAnulacion.ArmarHtml(null, "PN-1", "Org")
            .Should().Contain(">Hola,</p>").And.NotContain("Hola<br>");
    }

    [Fact]
    public void Las_imagenes_van_por_url_absoluta_desde_la_misma_clave_de_core_api()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:EmailAssets:BaseUrl"] = "https://qa.flitsas.online/email-assets/" })
            .Build();

        var html = CorreoDeCierre.ArmarHtml("Ana", "PN-1", "Org", LayoutCorreoFlit.BaseDesde(configuracion));

        html.Should().Contain("src=\"https://qa.flitsas.online/email-assets/flit-banner-correo.png\"")
            .And.Contain("src=\"https://qa.flitsas.online/email-assets/flit-logo-firma.png\" alt=\"FLIT\"")
            .And.NotContain("localhost");
    }

    [Fact]
    public void Sin_configuracion_usa_el_respaldo_local_como_core_api() =>
        LayoutCorreoFlit.BaseDesde(new ConfigurationBuilder().Build()).Should().Be("http://localhost:3000/email-assets");

    [Fact]
    public void El_marco_no_tiene_anchos_fijos_que_desborden_en_movil()
    {
        var html = CorreoDeAnulacion.ArmarHtml("Ana", "PN-1", "Org");

        html.Should().Contain("max-width:600px").And.NotContain("<style");
        Regex.Matches(html, @"(?<![-\w])width:\s*(\d+)px").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Should().OnlyContain(px => px <= 120, "solo el logo del pie tiene ancho fijo; lo demás es fluido");
    }

    /// <summary>Texto visible: sin etiquetas, entidades decodificadas y espacios colapsados.</summary>
    private static string Texto(string html)
    {
        var sinHead = Regex.Replace(html, "(?is)<head[^>]*>.*?</head>", " ");
        var sinEtiquetas = Regex.Replace(sinHead, "(?s)<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(sinEtiquetas), @"\s+", " ").Trim();
    }
}
