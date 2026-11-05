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
    public void La_variable_vieja_de_departamentos_no_impide_arrancar()
    {
        var opciones = OpcionesImpuesto.Desde(Config(("IMPUESTO_DEPARTAMENTOS", "99")));
        opciones.TieneRuta.Should().BeFalse();
    }

    [Fact]
    public void Sin_base_ni_ruta_el_transporte_no_esta_listo()
    {
        var opciones = OpcionesImpuesto.Desde(Config());
        opciones.TieneRuta.Should().BeFalse();
    }

    private static IConfiguration Config(params (string Clave, string Valor)[] pares)
    {
        var datos = pares.ToDictionary(p => p.Clave, p => (string?)p.Valor);
        return new ConfigurationBuilder().AddInMemoryCollection(datos).Build();
    }
}
