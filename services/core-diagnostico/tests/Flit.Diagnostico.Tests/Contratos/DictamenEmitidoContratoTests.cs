using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Contratos;

/// <summary>
/// Corte 7 — contrato de <c>diagnostico.dictamen.emitido</c> v1 (<c>contracts/asyncapi/diagnostico-events.v1.yaml</c>):
/// el <c>data</c> de <see cref="DictamenEmitido"/> coincide con <c>contracts/fixtures/bus/dictamen-emitido.v1.json</c> y el
/// AsyncAPI cierra el objeto. Sin placa, VIN, documentos ni el texto de los alertamientos.
/// </summary>
public sealed class DictamenEmitidoContratoTests
{
    private static readonly string[] Claves =
    [
        "diagnosticoId", "vehiculoId", "dictamenId", "version", "veredicto", "catalogoVersion", "modo", "tipoTramiteCodigo", "estadoRedaccion", "override",
    ];

    private static readonly string RutaFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bus", "dictamen-emitido.v1.json");

    [Fact]
    public void El_data_coincide_con_el_fixture_y_el_asyncapi_no_admite_propiedades_de_mas()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(RutaFixture))!;
        var esperado = fixture["data"]!;
        var evento = new DictamenEmitido(
            esperado["diagnosticoId"]!.GetValue<Guid>(),
            esperado["vehiculoId"]!.GetValue<Guid>(),
            esperado["dictamenId"]!.GetValue<Guid>(),
            esperado["version"]!.GetValue<int>(),
            "BLOQUEADO",
            1,
            "CON_ANALISIS",
            "TRASPASO_STANDARD",
            "PENDIENTE",
            EsOverride: false);

        var data = JsonSerializer.SerializeToNode(evento, EventEnvelope.JsonOptions)!;

        JsonNode.DeepEquals(data, esperado).Should().BeTrue($"el data {data.ToJsonString()} es el del fixture {esperado.ToJsonString()}");
        data.AsObject().Select(p => p.Key).Should().Equal(Claves);
        (fixture["type"]!.GetValue<string>(), fixture["producer"]!.GetValue<string>()).Should().Be((DictamenEmitido.Tipo, "diagnostico"));

        var yaml = File.ReadAllText(Path.Combine(Repo(), "contracts", "asyncapi", "diagnostico-events.v1.yaml"));
        var mensaje = yaml[yaml.IndexOf("name: diagnostico.dictamen.emitido", StringComparison.Ordinal)..];
        mensaje.Should().Contain("additionalProperties: false");
        foreach (var clave in Claves)
            mensaje.Should().Contain(clave);
        mensaje.Should().NotContain("placa").And.NotContain("vin").And.NotContain("documento");
    }

    // HU #14563 AC33 — ajuste_alertamientos es aditivo: solo sale en un ajuste con alertamientos, un consumidor del contrato
    // anterior sigue leyendo las mismas claves y el AsyncAPI lo declara. Sin motivo ni texto libre.
    [Fact]
    public void B7_ajuste_alertamientos_es_opcional_y_aditivo()
    {
        var evento = new DictamenEmitido(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2, "BLOQUEADO", 2, "CON_ANALISIS", "TRASPASO_STANDARD", "COMPLETADA",
            EsOverride: true, AjusteAlertamientos: true);

        var data = JsonSerializer.SerializeToNode(evento, EventEnvelope.JsonOptions)!.AsObject();

        data.Select(p => p.Key).Should().Equal([.. Claves, "ajuste_alertamientos"]);
        data["ajuste_alertamientos"]!.GetValue<bool>().Should().BeTrue();
        data.Select(p => p.Key).Should().NotContain(k => k.Contains("motivo", StringComparison.OrdinalIgnoreCase));

        var yaml = File.ReadAllText(Path.Combine(Repo(), "contracts", "asyncapi", "diagnostico-events.v1.yaml"));
        yaml[yaml.IndexOf("name: diagnostico.dictamen.emitido", StringComparison.Ordinal)..].Should().Contain("ajuste_alertamientos");
    }

    private static string Repo() =>
        Path.GetFullPath(Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "..", ".."));
}
