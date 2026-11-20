using Flit.Comparendos.Api.Infraestructura.Pdf;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// HU #14621: la cabecera con imagen y el marco de los correos salen según el CONJUNTO de plantillas de la empresa (FLIT o Renting), no según el canal de
/// correo (FlitSmtp/Renting, que solo es el transporte), y Renting no lleva ningún rastro de FLIT.
/// </summary>
public sealed class MembretePorConjuntoTests
{
    private const string Recursos = "http://127.0.0.1:4023/assets/correo";

    private static string Html(TipoPlantilla tipo, ConjuntoPlantillas conjunto)
    {
        var valores = ContextoPlantilla.Ejemplo();
        valores["empresa.razon_social"] = "Gestora de Ejemplo S.A.S.";
        return ServicioPlantillas.Armar(new PlantillaVersion { Asunto = "Asunto", CuerpoHtml = "<p>Señores {{ot.nombre}}: ver {{dp.radicado}}</p>" }, tipo, valores, conjunto, Recursos).Html;
    }

    [Fact]
    public void El_correo_del_DP_y_el_de_alerta_sin_representante_llevan_la_cabecera_de_FLIT_y_en_Renting_el_marco_neutro_sin_rastro_de_FLIT()
    {
        foreach (var tipo in new[] { TipoPlantilla.CorreoDpOt, TipoPlantilla.CorreoAlertaSinRl })
        {
            var flit = Html(tipo, ConjuntoPlantillas.Flit);
            flit.Should().Contain("flit-banner-correo.png").And.Contain("flit-logo-firma.png").And.Contain("soporte@flitsas.com").And.NotContain("Cordialmente");

            var renting = Html(tipo, ConjuntoPlantillas.Renting);
            renting.Should().Contain("renting-cabecera-dp.jpg").And.Contain("Correo automático, no lo respondas.");
            renting.Should().NotContain("renting-cabecera.jpg", "esa es la del aviso al conductor («Fotomulta pendiente de pago»)").And.NotContain("renting-pie.jpg").And.NotContain("tramitesvehiculos");
            renting.ToLowerInvariant().Should().NotContain("flit", $"{tipo} en Renting no lleva rastro de FLIT");
        }
    }

    [Fact]
    public void Los_correos_que_ya_usaban_los_marcos_siguen_igual()
    {
        Html(TipoPlantilla.CorreoAviso1, ConjuntoPlantillas.Flit).Should().Contain("flit-banner-correo.png").And.Contain("Cordialmente");
        Html(TipoPlantilla.CorreoAviso1, ConjuntoPlantillas.Renting).Should().Contain("renting-cabecera.jpg");
        Html(TipoPlantilla.CorreoCambioContraventorOt, ConjuntoPlantillas.Renting).Should().Contain("renting-cabecera.jpg");
    }

    [Fact]
    public void El_resumen_y_los_reportes_de_la_empresa_usan_su_marco()
    {
        var flit = HtmlCorreo.DeEmpresa(ConjuntoPlantillas.Flit, "Resumen", "<p>Cuerpo</p>", "Gestora de Ejemplo S.A.S.", Recursos);
        flit.Should().Contain("flit-banner-correo.png").And.Contain("Gestora de Ejemplo S.A.S.").And.Contain("Resumen").And.NotContain("Cordialmente");

        var renting = HtmlCorreo.DeEmpresa(ConjuntoPlantillas.Renting, "Resumen", "<p>Cuerpo</p>", "Gestora de Ejemplo S.A.S.", Recursos);
        renting.Should().Contain("Resumen").And.Contain("renting-cabecera-dp.jpg").And.NotContain("renting-cabecera.jpg").And.NotContain("renting-pie.jpg");
        renting.ToLowerInvariant().Should().NotContain("flit");
    }

    [Fact]
    public void Correos20_Los_PDF_de_FLIT_llevan_las_bandas_del_membrete_y_Renting_sigue_con_su_imagen_solo_en_la_autorizacion()
    {
        var flit = ServicioPlantillas.Membrete(ConjuntoPlantillas.Flit);
        flit.Fondo.Should().BeNull();
        flit.BandaSuperiorSvg.Should().StartWith("<?xml").And.Contain("<svg");
        flit.BandaInferiorSvg.Should().StartWith("<?xml").And.Contain("<svg").And.NotBe(flit.BandaSuperiorSvg);
        ServicioPlantillas.MembreteDeDocumento(ConjuntoPlantillas.Flit).Should().BeSameAs(flit);

        var renting = ServicioPlantillas.Membrete(ConjuntoPlantillas.Renting);
        renting.Fondo.Should().NotBeNullOrEmpty("la autorización de Renting conserva renting.jpg");
        renting.BandaSuperiorSvg.Should().BeNull();
        ServicioPlantillas.MembreteDeDocumento(ConjuntoPlantillas.Renting).Should().BeNull("el DP, el formato y la carta de Renting siguen sin membrete, como en V1");
    }

    [Fact]
    public void Correos20_Un_documento_largo_con_bandas_sale_en_varias_paginas()
    {
        var html = string.Concat(Enumerable.Range(1, 120).Select(i => $"<p>Párrafo {i} del derecho de petición con texto suficiente para ocupar una línea completa.</p>"));
        var pdf = HtmlAPdf.Generar(html, membrete: MembretePdf.Flit);
        pdf.Take(4).Should().Equal("%PDF"u8.ToArray());
        System.Text.Encoding.Latin1.GetString(pdf).Should().Contain("/Count 4", "las bandas de 2,54 cm le quitan espacio al cuerpo: 120 párrafos ocupan 4 hojas carta");
    }
}
