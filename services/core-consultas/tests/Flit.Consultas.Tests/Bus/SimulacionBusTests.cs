using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Simulacion;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>core-consultas con la simulación encendida en DEV (HU #13727, U3 = a).</summary>
public sealed class AnfitrionBusSimulado : AnfitrionBus
{
    public AnfitrionBusSimulado()
    {
        Extra[OpcionesSimulacion.ClaveHabilitada] = "true";
        Extra[OpcionesSimulacion.ClaveAmbiente] = "DEV";
    }
}

/// <summary>
/// HU #13727 (ADR-0075, U3 = a; T10) — con <c>Consultas:Simulacion:Habilitada</c> en DEV, los trabajos del diagnóstico recorren el
/// mismo camino del bus (parseo, medición con el llamador del sobre, minimización y resultado en la outbox) pero responde el
/// proveedor simulado: nada sale a Intempo ni a otro proveedor.
/// <para>Uso de ejemplo: <c>await _bus.EjecutarAsync&lt;ConsumidorTrabajosLote&gt;(AnfitrionBus.Trabajo(empresa, "runt", RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST003" }))</c>.</para>
/// </summary>
public sealed class SimulacionBusTests(AnfitrionBusSimulado bus) : IClassFixture<AnfitrionBusSimulado>
{
    [Fact]
    public async Task RUNT_simulado_no_llama_a_Intempo_publica_el_fixture_minimizado_y_mide_como_simulado()
    {
        var empresa = Guid.NewGuid();
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST003" });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        bus.Intempo.Pedidos.Should().BeEmpty("la simulación responde sin proveedor");
        var datos = (await bus.ResultadosAsync(sobre)).Should().ContainSingle().Subject.DataAs<ResultadoConsultaDiagnostico>();
        datos.Estado.Should().Be("OK");
        var json = datos.Respuesta!.Value.GetRawText();
        var respuesta = JsonParser.Default.Parse<ConsultarVehiculoIntempoResponse>(json);
        respuesta.Vehiculo.Plate.Should().Be("TST003");
        respuesta.Vehiculo.PropietariosActuales.Single().Should().BeEquivalentTo(new { NoDocumento = "999990003", DetallePropiedad = "LEASING", NombreCompleto = "" },
            o => o.ExcludingMissingMembers());
        json.Should().NotContain("FICTICIO").And.NotContain("PRUEBA DOS");
        (await bus.ConsumosAsync(empresa)).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Producto = "diagnostico", Fuente = FuentesBus.Runt, Proveedor = SimuladorConsultas.Proveedor });
    }

    [Fact]
    public async Task El_impuesto_simulado_responde_aunque_la_consulta_real_no_exista()
    {
        var sobre = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.Impuesto, RpcConsultas.Impuesto,
            new ConsultarImpuestoVehicularRequest { Opciones = new OpcionesConsulta(), Placa = new Placa { Valor = "TST001" }, DepartamentoCodigo = "05" }, esExpress: true);

        await bus.EjecutarAsync<ConsumidorTrabajosExpress>(sobre);

        var datos = (await bus.ResultadosAsync(sobre)).Single().DataAs<ResultadoConsultaDiagnostico>();
        datos.Estado.Should().Be("OK");
        JsonParser.Default.Parse<ConsultarImpuestoVehicularResponse>(datos.Respuesta!.Value.GetRawText()).MontoTotal.Should().Be(400_000);
    }

    [Theory]
    [InlineData("9999900017", "DeadlineExceeded")]
    public async Task Los_escenarios_de_error_simulados_son_resultados_ERROR(string documento, string grpcStatus)
    {
        var sobre = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.RuntPropietario, RpcConsultas.Conductor,
            new ConsultarConductorRequest { Documento = new DocumentoIdentidad { Tipo = "CC", Numero = documento } });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var datos = (await bus.ResultadosAsync(sobre)).Single().DataAs<ResultadoConsultaDiagnostico>();
        (datos.Estado, datos.GrpcStatus).Should().Be(("ERROR", grpcStatus));
    }

    [Fact]
    public async Task Una_solicitud_que_no_es_del_contrato_sigue_siendo_INVALID_ARGUMENT()
    {
        var sobre = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.Runt, RpcConsultas.PorPlaca, """{"placa":"TST001","vin":"X"}""");

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        (await bus.ResultadosAsync(sobre)).Single().DataAs<ResultadoConsultaDiagnostico>().GrpcStatus.Should().Be("InvalidArgument");
    }
}
