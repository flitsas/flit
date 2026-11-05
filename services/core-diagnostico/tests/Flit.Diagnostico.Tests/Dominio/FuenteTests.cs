using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13723 (F03-H1), AC1 (F03-C10) — las ocho fuentes del producto y ninguna más: la fuente de avalúo es <c>AVALUO</c>, no
/// <c>FASECOLDA</c> (12659 #1). Los códigos que valida la base salen de este enum (<c>EsquemaD2Tests</c>).
/// </summary>
public sealed class FuenteTests
{
    [Fact]
    public void Enum_tiene_exactamente_las_ocho_fuentes_y_no_FASECOLDA()
    {
        Enum.GetValues<Fuente>().Select(CodigosDeEnum.Codigo).Should().BeEquivalentTo(
        [
            "RUNT", "RUNT_PROPIETARIO", "RUNT_LOCATARIO", "SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO", "AVALUO",
            "IMPUESTO_DEPARTAMENTAL",
        ]);
        Enum.GetNames<Fuente>().Should().NotContain(n => n.Contains("Fasecolda", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Estados_motivos_y_disparadores_de_la_celda_son_los_del_plan()
    {
        Enum.GetValues<EstadoCelda>().Select(CodigosDeEnum.Codigo).Should().Equal("PENDIENTE", "EN_CURSO", "COMPLETADO", "ERROR", "NA");
        Enum.GetValues<MotivoNa>().Select(CodigosDeEnum.Codigo).Should().Equal("SERVICIO_PUBLICO", "DEPARTAMENTO_NO_SOPORTADO", "SIN_DOCUMENTO_LOCATARIO");
        Enum.GetValues<DisparadorIntento>().Select(CodigosDeEnum.Codigo).Should().Equal("AUTO", "REPROCESO", "MANUAL");
        Enum.GetValues<ServicioConsulta>().Select(CodigosDeEnum.Codigo).Should().Equal(
            "VERIFIK", "KYVERUM", "INTEMPO", "SMARTCLOUD", "FASECOLDA", "MERCADOLIBRE", "BASEGRAVABLE", "SCRAPING", "FLITFINES", "OTRO");
    }
}
