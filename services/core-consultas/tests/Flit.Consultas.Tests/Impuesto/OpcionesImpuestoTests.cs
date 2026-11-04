using Flit.Consultas.Api.Impuesto;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

public sealed class OpcionesImpuestoTests
{
    [Theory]
    [InlineData("http://host", "api/v1/tax/detail")]
    [InlineData("http://host/", "api/v1/tax/detail")]
    [InlineData("http://host", "/api/v1/tax/detail")]
    [InlineData("http://host/", "/api/v1/tax/detail")]
    public void La_url_queda_con_una_sola_barra(string baseUrl, string ruta) =>
        OpcionesImpuesto.Unir(baseUrl, ruta).Should().Be("http://host/api/v1/tax/detail");

    [Fact]
    public void Un_codigo_fuera_de_los_16_impide_armar_las_opciones()
    {
        var accion = () => OpcionesImpuesto.Desde(Config(("IMPUESTO_DEPARTAMENTOS", "99")));
        accion.Should().Throw<InvalidOperationException>().WithMessage("*IMPUESTO_DEPARTAMENTOS*");
    }

    [Fact]
    public void Sin_base_ni_ruta_el_transporte_no_esta_listo()
    {
        var opciones = OpcionesImpuesto.Desde(Config());
        opciones.TieneRuta.Should().BeFalse();
        opciones.Departamentos.Should().BeEmpty();
    }

    private static IConfiguration Config(params (string Clave, string Valor)[] pares)
    {
        var datos = pares.ToDictionary(p => p.Clave, p => (string?)p.Valor);
        return new ConfigurationBuilder().AddInMemoryCollection(datos).Build();
    }
}
