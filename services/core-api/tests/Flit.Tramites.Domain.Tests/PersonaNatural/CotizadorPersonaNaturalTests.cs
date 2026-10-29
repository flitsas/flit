using Flit.Tramites.Domain.Tramites.PersonaNatural;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.PersonaNatural;

/// <summary>HU #14043 (Épica #14021) — el cálculo único de la cotización de 2 líneas.</summary>
public sealed class CotizadorPersonaNaturalTests
{
    private static readonly Guid Tipo = Guid.NewGuid();
    private static readonly Guid Organismo = Guid.NewGuid();

    private static TarifaVigente T(string concepto, long valor, Guid? tipo = null, Guid? organismo = null) =>
        new(concepto, tipo, organismo, valor, Guid.NewGuid());

    private static TarifaVigente[] Base(long derechos = 100, long flit = 10) =>
        [T("DERECHOS_TRANSITO", derechos, Tipo), T("SERVICIO_FLIT", flit, Tipo)];

    [Fact]
    public void SinTarifaVigente_RespondeTarifaNoVigente_SinInventarPrecios()
    {
        CotizadorPersonaNatural.Calcular(Tipo, Organismo, true, [], []).Error.Should().Be("tarifa_no_vigente");
        CotizadorPersonaNatural.Calcular(Tipo, Organismo, true, [], [T("DERECHOS_TRANSITO", 1, Tipo)]).Error.Should().Be("tarifa_no_vigente", "falta el Servicio FLIT");
    }

    [Fact]
    public void DosLineasYTotal_ConSubtituloEnElServicioFlit()
    {
        var c = CotizadorPersonaNatural.Calcular(Tipo, Organismo, true, [], Base()).Cotizacion!;

        c.Lineas.Select(l => l.Codigo).Should().Equal("DERECHOS_TRANSITO", "SERVICIO_FLIT");
        c.Lineas[1].Subtitulo.Should().Be(CotizadorPersonaNatural.SubtituloServicioFlit);
        c.TotalCop.Should().Be(110);
    }

    [Fact]
    public void PrecedenciaDeLaTarifa_TipoYOrganismo_ManejaSobreTipoGeneral_ySobreCualquierTipo()
    {
        var tarifas = new[]
        {
            T("DERECHOS_TRANSITO", 100, Tipo),
            T("DERECHOS_TRANSITO", 120, Tipo, Organismo),
            T("DERECHOS_TRANSITO", 999, Tipo, Guid.NewGuid()), // excepción de otro organismo: no aplica
            T("SERVICIO_FLIT", 10, Tipo),
            T("ENVIO_DOCUMENTO", 5),
            T("ENVIO_DOCUMENTO", 7, organismo: Organismo),
        };

        var c = CotizadorPersonaNatural.Calcular(Tipo, Organismo, true, ["ENVIO_DOCUMENTO"], tarifas).Cotizacion!;

        c.Lineas.Single(l => l.Codigo == "DERECHOS_TRANSITO").ValorCop.Should().Be(120);
        c.Lineas.Single(l => l.Codigo == "ENVIO_DOCUMENTO").ValorCop.Should().Be(7, "el valor propio del organismo manda sobre el general");
    }

    [Fact]
    public void LasTransformacionesNoSuman_SiElTipoNoLasPermite_AunqueTenganValor()
    {
        var tarifas = Base().Append(T("CAMBIO_COLOR", 1_000)).Append(T("ENVIO_DOCUMENTO", 20)).ToArray();

        var c = CotizadorPersonaNatural.Calcular(Tipo, Organismo, permiteTransformaciones: false, ["CAMBIO_COLOR", "ENVIO_DOCUMENTO"], tarifas).Cotizacion!;

        c.TotalCop.Should().Be(130);
        c.AdicionalesNoAplicados.Should().Equal("CAMBIO_COLOR");
    }

    [Fact]
    public void UnAdicionalSinTarifaOInventado_NoSuma_YSeReporta()
    {
        var c = CotizadorPersonaNatural.Calcular(Tipo, Organismo, true, ["cambio_color", "NO_EXISTE", "NO_EXISTE"], Base()).Cotizacion!;

        c.TotalCop.Should().Be(110);
        c.AdicionalesNoAplicados.Should().BeEquivalentTo("CAMBIO_COLOR", "NO_EXISTE");
    }

    [Fact]
    public void MismoOrganismoYTipo_DaElMismoTotalYLaMismaVersion_SinImportarElOrdenDeLasTarifas()
    {
        var tarifas = Base(77, 33);

        var a = CotizadorPersonaNatural.Calcular(Tipo, Organismo, true, [], tarifas).Cotizacion!;
        var b = CotizadorPersonaNatural.Calcular(Tipo, Organismo, true, [], tarifas.Reverse().ToArray()).Cotizacion!;

        b.TotalCop.Should().Be(a.TotalCop);
        b.VersionTarifa.Should().Be(a.VersionTarifa);
    }
}
