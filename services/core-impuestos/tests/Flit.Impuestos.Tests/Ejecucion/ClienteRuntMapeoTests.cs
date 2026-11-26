using Flit.Consultas.Grpc.V1;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Ejecucion;

/// <summary>
/// HU #14694 (R3) — <see cref="ClienteRuntGrpc.Mapear"/> lleva <c>informacion_general.tipo_servicio</c> del RUNT a
/// <see cref="DatosRunt.TipoServicio"/> (texto vacío ⇒ null), para que el consumidor sepa si el vehículo es de servicio público.
/// </summary>
/// <example>
/// <code>
/// ClienteRuntGrpc.Mapear(vehiculo).TipoServicio; // "PUBLICO"
/// </code>
/// </example>
public sealed class ClienteRuntMapeoTests
{
    [Theory]
    [InlineData("PUBLICO", "PUBLICO")]
    [InlineData(" Particular ", "Particular")]
    [InlineData("", null)]
    public void Mapea_el_tipo_de_servicio_del_RUNT(string valor, string? esperado)
    {
        var vehiculo = new VehiculoIntempo { InformacionGeneral = new InformacionGeneralIntempo { TipoServicio = valor, Modelo = "2019" } };

        var datos = ClienteRuntGrpc.Mapear(vehiculo);

        datos.TipoServicio.Should().Be(esperado);
        datos.Modelo.Should().Be("2019");
    }

    [Fact]
    public void Sin_informacion_general_el_tipo_de_servicio_es_null()
    {
        ClienteRuntGrpc.Mapear(new VehiculoIntempo()).TipoServicio.Should().BeNull();
    }
}
