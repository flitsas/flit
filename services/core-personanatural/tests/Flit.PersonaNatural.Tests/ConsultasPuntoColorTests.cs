using System.Text.Json;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Punto;
using FluentAssertions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>La consulta de vehículo del punto devuelve el color (aditivo) y nada de personas, con una sola llamada al RUNT.</summary>
public sealed class ConsultasPuntoColorTests
{
    [Fact]
    public async Task VehiculoDelPunto_IncluyeColor_SinDatosPersonales_YUnaSolaConsulta()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = () => ResultadosFalsos.VehiculoCompleto(prenda: true) };
        var servicio = new ConsultasPuntoService(new ConsultaConductorService(runt), runt);

        var v = await servicio.VehiculoAsync("ABC123", null, new DocumentoConsulta("CC", "1098765432"), TestContext.Current.CancellationToken);

        v.Color.Should().Be("ROJO");
        v.Placa.Should().Be("ABC123");
        v.Marca.Should().Be("MARCA-FICTICIA");
        v.TienePrenda.Should().BeTrue();
        var json = JsonSerializer.Serialize(v, JsonSerializerOptions.Web);
        json.Should().Contain("\"color\":\"ROJO\"")
            .And.NotContainAny("1098765432", "PROPIETARIO", "owner", "9BWZZZ377VT004251", "MOTOR-FICTICIO");
        runt.LlamadasVehiculo.Should().ContainSingle();
    }
}
