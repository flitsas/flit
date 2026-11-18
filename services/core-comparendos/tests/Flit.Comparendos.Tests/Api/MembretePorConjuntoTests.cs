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
            flit.Should().Contain("flit-cabecera.png").And.Contain("flit-logo.png").And.Contain("soporte@flitsas.com").And.NotContain("Cordialmente");

            var renting = Html(tipo, ConjuntoPlantillas.Renting);
            renting.Should().NotContain("renting-cabecera.jpg", "la cabecera de Renting es la del aviso al conductor («Fotomulta pendiente de pago»)").And.NotContain("renting-pie.jpg");
            renting.ToLowerInvariant().Should().NotContain("flit", $"{tipo} en Renting no lleva rastro de FLIT");
        }
    }

    [Fact]
    public void Los_correos_que_ya_usaban_los_marcos_siguen_igual()
    {
        Html(TipoPlantilla.CorreoAviso1, ConjuntoPlantillas.Flit).Should().Contain("flit-cabecera.png").And.Contain("Cordialmente");
        Html(TipoPlantilla.CorreoAviso1, ConjuntoPlantillas.Renting).Should().Contain("renting-cabecera.jpg");
        Html(TipoPlantilla.CorreoCambioContraventorOt, ConjuntoPlantillas.Renting).Should().Contain("renting-cabecera.jpg");
    }

    [Fact]
    public void El_resumen_y_los_reportes_de_la_empresa_usan_su_marco()
    {
        var flit = HtmlCorreo.DeEmpresa(ConjuntoPlantillas.Flit, "Resumen", "<p>Cuerpo</p>", "Gestora de Ejemplo S.A.S.", Recursos);
        flit.Should().Contain("flit-cabecera.png").And.Contain("Gestora de Ejemplo S.A.S.").And.Contain("Resumen").And.NotContain("Cordialmente");

        var renting = HtmlCorreo.DeEmpresa(ConjuntoPlantillas.Renting, "Resumen", "<p>Cuerpo</p>", "Gestora de Ejemplo S.A.S.", Recursos);
        renting.Should().Contain("Resumen");
        renting.ToLowerInvariant().Should().NotContain("flit");
    }
}
