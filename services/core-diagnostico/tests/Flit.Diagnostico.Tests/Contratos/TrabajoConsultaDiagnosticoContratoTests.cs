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
/// claro pero sin nombres ni direcciones de persona, salvo el nombre del propietario que el impuesto envía a propósito
/// (P9) junto con el nombre del organismo y del departamento. El resto de las fuentes no los lleva. PostgreSQL real.
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
        TrabajoConsultaDiagnostico.Clave(FuentesBus.Impuesto, esExpress: false).Should().Be("consultas.diagnostico-impuesto.impuesto");
        TrabajoConsultaDiagnostico.Clave(FuentesBus.Impuesto, esExpress: true).Should().Be("consultas.diagnostico-impuesto-express.impuesto");
        TrabajoConsultaDiagnostico.Clave(FuentesBus.Impuesto, esExpress: false)
            .StartsWith(TrabajoConsultaDiagnostico.Prefijo + ".", StringComparison.Ordinal).Should().BeFalse("la cola general no recibe el impuesto");
        fixture["producer"]!.GetValue<string>().Should().Be("diagnostico");
    }

    [Fact]
    public async Task Ningun_trabajo_lleva_nombres_ni_direcciones()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        // La RUNT de los tres vehículos sale al encolar y, al cerrarla, las liberadas publican el suyo (tanda B). El impuesto quedó N/A
        // (ningún organismo de prueba deriva su departamento): con el departamento ya fijado, se vuelve a publicar.
        var lote = await ConstructoresSolicitudTests.LoteConRuntAsync(api, empresa, ct);
        await using (var scope = OutboxDePrueba.Scope(api, empresa.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
            var publicador = scope.ServiceProvider.GetRequiredService<PublicadorCeldas>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaccion = await db.Database.BeginTransactionAsync(ct);
                foreach (var celda in await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote && c.Fuente == Domain.Consultas.Fuente.ImpuestoDepartamental).ToListAsync(ct))
                {
                    (celda.Estado, celda.MotivoNa) = (Domain.Consultas.EstadoCelda.Pendiente, null);
                    await publicador.PublicarAsync(celda, ct, celda.VersionTablero);
                }

                await db.SaveChangesAsync(ct);
                await transaccion.CommitAsync(ct);
            });
        }

        var trabajos = await OutboxDePrueba.TrabajosAsync(api, ct);
        trabajos.Select(t => t.Data.GetProperty("fuente").GetString()).Distinct().Should().BeEquivalentTo(
            [FuentesBus.Runt, FuentesBus.RuntPropietario, FuentesBus.RuntLocatario, FuentesBus.Rues, FuentesBus.SimitVehiculo,
                FuentesBus.SimitPropietario, FuentesBus.SimitLocatario, FuentesBus.Avaluo, FuentesBus.Impuesto], "hay trabajos de todas las fuentes");
        foreach (var trabajo in trabajos)
        {
            var impuesto = trabajo.Data.GetProperty("fuente").GetString() == FuentesBus.Impuesto;
            foreach (var nombre in Nombres)
            {
                if (impuesto && nombre is "PERSONA PRUEBA UNO" or "LEASING FICTICIO SA")
                    continue;
                trabajo.Payload.Should().NotContainEquivalentOf(nombre);
            }

            var claves = Claves(JsonNode.Parse(trabajo.Payload)!);
            if (impuesto)
                claves = claves.Where(c => c is not ("propietarioNombre" or "organismoNombre" or "departamentoNombre"));
            claves.Should().NotContain(c => ClavesProhibidas.Any(p => c.Contains(p, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static IEnumerable<string> Claves(JsonNode nodo) => nodo switch
    {
        JsonObject objeto => objeto.SelectMany(p => p.Value is null ? [p.Key] : Claves(p.Value).Prepend(p.Key)),
        JsonArray arreglo => arreglo.Where(e => e is not null).SelectMany(e => Claves(e!)),
        _ => [],
    };
}
