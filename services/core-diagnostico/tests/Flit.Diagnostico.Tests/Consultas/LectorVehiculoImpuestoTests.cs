using Flit.Diagnostico.Api.Modulos.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>Clase, marca, servicio y combustible de <c>informacion_general</c> (D3).</summary>
public sealed class LectorVehiculoImpuestoTests
{
    [Fact]
    public void Lee_snake_y_camel()
    {
        var snake = """
            {"vehiculo":{"informacion_general":{
              "clase_vehiculo":"AUTOMOVIL","marca":"MAZDA","tipo_servicio":"PARTICULAR","tipo_combustible":"GASOLINA"}}}
            """;
        var camel = """
            {"vehiculo":{"informacionGeneral":{
              "claseVehiculo":"MOTO","marca":"YAMAHA","tipoServicio":"PUBLICO","tipoCombustible":"ELECTRICO"}}}
            """;

        LectorVehiculoImpuesto.Leer(snake).Should().BeEquivalentTo(
            new LectorVehiculoImpuesto.VehiculoRunt("AUTOMOVIL", "MAZDA", "PARTICULAR", "GASOLINA"));
        LectorVehiculoImpuesto.Leer(camel).Should().BeEquivalentTo(
            new LectorVehiculoImpuesto.VehiculoRunt("MOTO", "YAMAHA", "PUBLICO", "ELECTRICO"));
    }

    [Fact]
    public void Sin_informacion_general_devuelve_null() =>
        LectorVehiculoImpuesto.Leer("""{"vehiculo":{}}""").Should().BeNull();
}
