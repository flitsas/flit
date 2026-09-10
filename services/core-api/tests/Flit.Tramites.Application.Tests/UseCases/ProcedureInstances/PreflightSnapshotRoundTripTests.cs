using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// El snapshot del pre-vuelo se GUARDA como JSON y se vuelve a leer: lo que el panel muestra no es
/// lo que el proveedor acaba de responder, sino lo que quedó persistido.
///
/// <para>Por eso el respaldo de los checks (<c>Datos</c>) tiene que sobrevivir ese viaje. Si se
/// perdiera, la tarjeta en verde volvería a quedar vacía —el síntoma exacto que este respaldo vino a
/// resolver— y el fallo sería invisible: no hay error, solo un hueco.</para>
/// </summary>
public sealed class PreflightSnapshotRoundTripTests
{
    [Fact]
    public void LosDatosDelProveedorSobrevivenAlGuardadoYLectura()
    {
        var checks = new List<PreflightCheckDto>
        {
            new("soat", "SOAT", "ok", "kyverum_runt", null, null,
                [
                    new CheckDato("Vigente hasta", "2027/01/23"),
                    new CheckDato("Póliza", "3506349600"),
                    new CheckDato("Aseguradora", "AXA COLPATRIA SEGUROS SA"),
                ]),
        };

        // Mismo par serializar/deserializar que usa el guardado del snapshot
        // (`JsonSerializer` con opciones por defecto, en ambos extremos).
        var json = JsonSerializer.Serialize(checks);
        var leidos = JsonSerializer.Deserialize<List<PreflightCheckDto>>(json)!;

        leidos.Should().ContainSingle();
        leidos[0].Datos.Should().NotBeNull();
        leidos[0].Datos!.Should().HaveCount(3);
        leidos[0].Datos![0].Etiqueta.Should().Be("Vigente hasta");
        leidos[0].Datos![0].Valor.Should().Be("2027/01/23");
    }

    [Fact]
    public void UnSnapshotAnteriorAlRespaldoSeLeeSinRomper()
    {
        // Los expedientes con un pre-vuelo ya corrido no traen la llave: se leen con `Datos` en null
        // y la tarjeta cae al mensaje de siempre, como antes de este cambio.
        var json = """
            [{"Key":"soat","Label":"SOAT","Status":"ok","Source":"kyverum_runt","Message":null}]
            """;

        var leidos = JsonSerializer.Deserialize<List<PreflightCheckDto>>(json)!;

        leidos.Should().ContainSingle();
        leidos[0].Datos.Should().BeNull();
    }
}
