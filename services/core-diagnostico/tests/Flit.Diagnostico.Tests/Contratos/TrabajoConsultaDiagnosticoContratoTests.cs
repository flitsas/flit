using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Contratos;

/// <summary>
/// HU #13727 (ADR-0075, U1 = b) — contrato del trabajo <c>consultas.diagnostico[-express].{fuente}</c>
/// (<c>contracts/asyncapi/consultas-events.v1.yaml</c>): el <c>data</c> que publica <see cref="PublicadorCeldas"/> coincide en
/// claves y formato con el fixture compartido <c>contracts/fixtures/bus/trabajo-consulta-diagnostico.v1.json</c>, y viaja en
/// claro pero sin nombres ni direcciones: con el RUNT simulado (propietario «LEASING FICTICIO SA», locatario «LOCATARIO PRUEBA
/// DOS», propietario natural «PERSONA PRUEBA UNO»; las respuestas de prueba no traen direcciones) ninguno de esos valores, ni
/// una clave de nombre, razón social, dirección o contacto, aparece en ningún trabajo de ninguna fuente. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TrabajoConsultaDiagnosticoContratoTests
/// </code>
/// </example>
public sealed class TrabajoConsultaDiagnosticoContratoTests
{
    private static readonly string RutaFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bus", "trabajo-consulta-diagnostico.v1.json");

    private static readonly string[] Nombres = ["LEASING FICTICIO SA", "LOCATARIO PRUEBA DOS", "PERSONA PRUEBA UNO"];

    private static readonly string[] ClavesProhibidas = ["nombre", "name", "razon", "direccion", "address", "telefono", "phone", "correo", "email"];

    [Fact]
    public void El_data_serializado_coincide_con_el_fixture_en_claves_y_formato()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(RutaFixture))!;
        var esperado = fixture["data"]!;
        var trabajo = TrabajoConsultaDiagnostico.Crear(
            esperado["trabajoId"]!.GetValue<Guid>(), esperado["consultaId"]!.GetValue<Guid>(),
            new SolicitudConsulta(FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST001" }));

        var data = JsonSerializer.SerializeToNode(trabajo, EventEnvelope.JsonOptions)!;

        JsonNode.DeepEquals(data, esperado).Should().BeTrue($"el data {data.ToJsonString()} es el del fixture {esperado.ToJsonString()}");
        fixture["type"]!.GetValue<string>().Should().Be(TrabajoConsultaDiagnostico.Clave(FuentesBus.Runt, esExpress: false));
        TrabajoConsultaDiagnostico.Clave(FuentesBus.Runt, esExpress: true).Should().Be("consultas.diagnostico-express.runt");
        fixture["producer"]!.GetValue<string>().Should().Be("diagnostico");
    }

    [Fact]
    public async Task Ningun_trabajo_lleva_nombres_ni_direcciones()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await ConstructoresSolicitudTests.LoteConRuntAsync(api, empresa, ct);

        await using (var scope = OutboxDePrueba.Scope(api, empresa.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
            var publicador = scope.ServiceProvider.GetRequiredService<PublicadorCeldas>();
            foreach (var celda in await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).ToListAsync(ct))
                await publicador.PublicarAsync(celda, ct);
            await db.SaveChangesAsync(ct);
        }

        var trabajos = await OutboxDePrueba.TrabajosAsync(api, ct);
        trabajos.Select(t => t.Data.GetProperty("fuente").GetString()).Distinct().Should().BeEquivalentTo(
            [FuentesBus.Runt, FuentesBus.RuntPropietario, FuentesBus.RuntLocatario, FuentesBus.Rues, FuentesBus.SimitVehiculo,
                FuentesBus.SimitPropietario, FuentesBus.SimitLocatario, FuentesBus.Avaluo, FuentesBus.Impuesto], "hay trabajos de todas las fuentes");
        foreach (var trabajo in trabajos)
        {
            foreach (var nombre in Nombres)
                trabajo.Payload.Should().NotContainEquivalentOf(nombre);
            Claves(JsonNode.Parse(trabajo.Payload)!).Should().NotContain(c => ClavesProhibidas.Any(p => c.Contains(p, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static IEnumerable<string> Claves(JsonNode nodo) => nodo switch
    {
        JsonObject objeto => objeto.SelectMany(p => p.Value is null ? [p.Key] : Claves(p.Value).Prepend(p.Key)),
        JsonArray arreglo => arreglo.Where(e => e is not null).SelectMany(e => Claves(e!)),
        _ => [],
    };
}
