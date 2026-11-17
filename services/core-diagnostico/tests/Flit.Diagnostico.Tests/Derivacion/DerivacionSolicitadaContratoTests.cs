using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Diagnostico.Api.Modulos.Derivacion.Bus;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Bus;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 3 (diseño §8, ADR-0084, sin enmienda a U1 de ADR-0075 ni a R-2 de ADR-0072) — contrato de
/// <c>diagnostico.derivacion.solicitada</c> v1: el <c>data</c> de <see cref="DerivacionSolicitada"/> coincide con el fixture compartido
/// <c>contracts/fixtures/bus/diagnostico.derivacion.solicitada.v1.json</c> y SOLO lleva ids (falla si el evento gana un campo que no
/// sea un uuid: ni placa, ni VIN, ni documento). La topología del broker (<c>deploy/rabbitmq/definitions.json</c>) declara la cola
/// <c>diagnostico.derivaciones</c> atada a <c>flit.diagnostico</c> con la llave del evento, su TTL de 24 h hacia la DLQ y la DLQ de 7
/// días; la AsyncAPI de Diagnóstico publica el canal.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DerivacionSolicitadaContratoTests
/// </code>
/// </example>
public sealed class DerivacionSolicitadaContratoTests
{
    private static readonly string RutaFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bus", "diagnostico.derivacion.solicitada.v1.json");

    [Fact]
    public void El_data_coincide_con_el_fixture_y_solo_lleva_ids()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(RutaFixture))!;
        var esperado = fixture["data"]!;
        var evento = new DerivacionSolicitada(
            esperado["operacionId"]!.GetValue<Guid>(), esperado["itemId"]!.GetValue<Guid>(), esperado["vehiculoId"]!.GetValue<Guid>());

        var data = JsonSerializer.SerializeToNode(evento, EventEnvelope.JsonOptions)!;

        JsonNode.DeepEquals(data, esperado).Should().BeTrue($"el data {data.ToJsonString()} es el del fixture {esperado.ToJsonString()}");
        data.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["operacionId", "itemId", "vehiculoId"], "solo ids (diseño §8)");
        typeof(DerivacionSolicitada).GetProperties().Should().OnlyContain(p => p.PropertyType == typeof(Guid),
            "un campo que no sea id rompe el contrato sin PII del broker");
        (fixture["type"]!.GetValue<string>(), fixture["producer"]!.GetValue<string>(), fixture["version"]!.GetValue<int>())
            .Should().Be((DerivacionSolicitada.Tipo, DerivacionSolicitada.Productor, DerivacionSolicitada.VersionContrato));
    }

    [Fact]
    public void El_broker_declara_la_cola_su_binding_y_sus_politicas()
    {
        DefinicionesBroker.Colas.Should().Contain(DerivacionSolicitada.Cola);
        DefinicionesBroker.Bindings.Should().Contain(("flit.diagnostico", DerivacionSolicitada.Cola, DerivacionSolicitada.Tipo));

        var ttl = DefinicionesBroker.PoliticaDe(DerivacionSolicitada.Cola);
        ttl.Should().NotBeNull();
        ttl!.TtlMs.Should().Be(86_400_000);
        ttl.Texto("dead-letter-exchange").Should().Be($"{DerivacionSolicitada.Cola}.reintentos", "el exchange de reintentos que declara el SDK");
        ttl.Texto("dead-letter-routing-key").Should().Be($"{DerivacionSolicitada.Cola}.dlq");
        DefinicionesBroker.PoliticaDe($"{DerivacionSolicitada.Cola}.dlq")!.TtlMs.Should().Be(604_800_000, "dlq-7d como ADR-0075");
        DefinicionesBroker.PoliticaDe($"{DerivacionSolicitada.Cola}.retry.1").Should().BeNull("las colas de espera las gobierna el SDK con su TTL");
    }

    [Fact]
    public void La_asyncapi_de_diagnostico_publica_el_canal()
    {
        var raiz = Path.GetFullPath(Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "..", ".."));
        var asyncapi = File.ReadAllText(Path.Combine(raiz, "contracts", "asyncapi", "diagnostico-events.v1.yaml"));

        asyncapi.Should().Contain(DerivacionSolicitada.Tipo).And.Contain("operacionId").And.Contain("itemId").And.Contain("vehiculoId");
    }
}
