using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Contratos;

/// <summary>
/// HU #13743 (ADR-0075, Q8) — contrato de <c>diagnostico.lote.terminado</c> v1 (<c>contracts/asyncapi/diagnostico-events.v1.yaml</c>):
/// el <c>data</c> de <see cref="LoteTerminado"/> coincide con el fixture compartido
/// <c>contracts/fixtures/bus/lote-terminado.v1.json</c> y solo tiene las claves del contrato (id, estado, versión, fecha y
/// totales): ni placas, ni VIN, ni documentos, ni nombres.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~LoteTerminadoContratoTests
/// </code>
/// </example>
public sealed class LoteTerminadoContratoTests
{
    private static readonly string RutaFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bus", "lote-terminado.v1.json");

    [Fact]
    public void El_data_coincide_con_el_fixture_y_no_lleva_datos_personales()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(RutaFixture))!;
        var esperado = fixture["data"]!;
        var evento = new LoteTerminado(
            esperado["diagnosticoId"]!.GetValue<Guid>(), "COMPLETADO", EsExpress: false, Version: 42,
            esperado["terminadoEn"]!.GetValue<DateTimeOffset>(), Vehiculos: 200,
            new Dictionary<string, int> { ["COMPLETADO"] = 1500, ["ERROR"] = 80, ["NA"] = 20 },
            new Dictionary<string, int> { ["TIMEOUT"] = 3 });

        var data = JsonSerializer.SerializeToNode(evento, EventEnvelope.JsonOptions)!;

        JsonNode.DeepEquals(data, esperado).Should().BeTrue($"el data {data.ToJsonString()} es el del fixture {esperado.ToJsonString()}");
        data.AsObject().Select(p => p.Key).Should().BeEquivalentTo(
            ["diagnosticoId", "estado", "esExpress", "version", "terminadoEn", "vehiculos", "celdas", "erroresPorTipo"], "solo las claves del contrato");
        (fixture["type"]!.GetValue<string>(), fixture["producer"]!.GetValue<string>()).Should().Be((LoteTerminado.Tipo, "diagnostico"));
    }
}
