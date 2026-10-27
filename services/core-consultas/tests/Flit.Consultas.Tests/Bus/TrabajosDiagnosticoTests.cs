using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #13876 (ADR-0075, Q3, Q4 con U2 = a) — los consumidores de trabajos del diagnóstico de core-consultas: parsean el
/// <c>*Request</c>, ejecutan UNA vez el <c>*Core</c> del rpc con el llamador del sobre (<c>svc-diagnostico</c> y la empresa del
/// <c>tenantId</c>), miden el consumo, aplican los overrides de esa empresa y publican <c>consultas.resultado.diagnostico</c> con la
/// respuesta COMPLETA (HU #14087 AC8: proto-JSON con los nombres del .proto y los valores por defecto) en la misma transacción. Un error nunca sale como excepción: es un resultado ERROR con su
/// <c>grpcStatus</c>, sin el mensaje.
/// <para>Uso de ejemplo: <c>await _bus.EjecutarAsync&lt;ConsumidorTrabajosLote&gt;(AnfitrionBus.Trabajo(empresa, "runt", RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST401" }))</c>.</para>
/// </summary>
public sealed class TrabajosDiagnosticoTests(AnfitrionBus bus) : IClassFixture<AnfitrionBus>
{
    [Fact]
    public async Task RUNT_por_placa_publica_OK_con_la_respuesta_completa_y_mide_con_el_llamador_del_sobre()
    {
        var empresa = Guid.NewGuid();
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var resultado = (await bus.ResultadosAsync(sobre)).Should().ContainSingle().Subject;
        (resultado.TenantId, resultado.Producer, resultado.CorrelationId, resultado.Version).Should().Be((empresa, "consultas", sobre.CorrelationId, 1));
        var datos = resultado.DataAs<ResultadoConsultaDiagnostico>();
        var trabajo = sobre.DataAs<TrabajoConsultaDiagnostico>();
        (datos.TrabajoId, datos.ConsultaId, datos.PersonaId, datos.Fuente, datos.Estado, datos.GrpcStatus, datos.DesdeCache)
            .Should().Be((trabajo.TrabajoId, trabajo.ConsultaId, null, FuentesBus.Runt, "OK", null, false));
        datos.DuracionMs.Should().BeGreaterThanOrEqualTo(0);
        var respuesta = JsonParser.Default.Parse<ConsultarVehiculoIntempoResponse>(datos.Respuesta!.Value.GetRawText());
        (respuesta.Resultado, respuesta.Vehiculo.Plate).Should().Be((ResultadoRuntIntempo.Encontrado, AnfitrionBus.PlacaEncontrada));
        datos.Respuesta!.Value.GetProperty("vehiculo").TryGetProperty("informacion_general", out _).Should().BeTrue("HU #14087 AC8: nombres del .proto");

        var consumo = (await bus.ConsumosAsync(empresa)).Should().ContainSingle().Subject;
        (consumo.Producto, consumo.Fuente, consumo.Proveedor, consumo.Resultado).Should().Be(("diagnostico", "runt_intempo_placa", "intempo", "con_datos"));
    }

    [Fact]
    public async Task El_consumidor_del_Express_es_otro_tipo_y_hace_lo_mismo()
    {
        typeof(ConsumidorTrabajosExpress).Should().NotBe<ConsumidorTrabajosLote>("el SDK deduplica los consumidores por tipo (H3)");
        var empresa = Guid.NewGuid();
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorVin, new ConsultarPorVinRequest { Vin = "9ZZPRUEBA00000401" }, esExpress: true);

        await bus.EjecutarAsync<ConsumidorTrabajosExpress>(sobre);

        (await bus.ResultadosAsync(sobre)).Should().ContainSingle().Which.DataAs<ResultadoConsultaDiagnostico>().Estado.Should().Be("OK");
        (await bus.ConsumosAsync(empresa)).Should().ContainSingle().Which.Fuente.Should().Be("runt_intempo_vin");
    }

    [Fact]
    public async Task Si_la_transaccion_se_revierte_no_queda_ni_el_resultado_ni_el_consumo()
    {
        var empresa = Guid.NewGuid();
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre, confirmar: false);

        (await bus.ResultadosAsync(sobre)).Should().BeEmpty();
        (await bus.ConsumosAsync(empresa)).Should().BeEmpty();
    }

    [Fact]
    public async Task El_proveedor_caido_dentro_de_la_respuesta_es_OK_con_su_error_para_que_Diagnostico_decida()
    {
        var sobre = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaCaida });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var datos = (await bus.ResultadosAsync(sobre)).Single().DataAs<ResultadoConsultaDiagnostico>();
        datos.Estado.Should().Be("OK");
        var respuesta = JsonParser.Default.Parse<ConsultarVehiculoIntempoResponse>(datos.Respuesta!.Value.GetRawText());
        respuesta.Resultado.Should().Be(ResultadoRuntIntempo.Error);
        // HU #14087 AC8: el resultado OK lleva la respuesta completa, también el mensaje técnico del proveedor dentro del *Response
        // (antes lo quitaba la minimización). Un ERROR sigue sin respuesta ni mensaje: solo el grpcStatus.
        respuesta.HasMensajeError.Should().BeTrue("la respuesta OK viaja completa");
    }

    public static TheoryData<string, string, string, string> Errores => new()
    {
        { "placa vacía", RpcConsultas.PorPlaca, JsonFormatter.Default.Format(new ConsultarPorPlacaRequest()), "InvalidArgument" },
        { "avalúo sin placa ni VIN", RpcConsultas.Avaluos, JsonFormatter.Default.Format(new ConsultarAvaluosRequest { Modelo = 2020 }), "InvalidArgument" },
        { "proveedor desconocido", RpcConsultas.Multas, JsonFormatter.Default.Format(Multas("no_existe")), "InvalidArgument" },
        { "solicitud con un campo que no es del contrato", RpcConsultas.PorPlaca, """{"placa":"TST401","nombre":"X"}""", "InvalidArgument" },
        { "solicitud que no es del rpc", RpcConsultas.Rues, """{"placa":"TST401"}""", "InvalidArgument" },
        { "impuesto aún sin consulta real (C3)", RpcConsultas.Impuesto, JsonFormatter.Default.Format(new ConsultarImpuestoVehicularRequest { Placa = new Placa { Valor = "TST401" }, DepartamentoCodigo = "05" }), "Unimplemented" },
        { "rpc desconocido", "ConsultasService/ConsultarVehiculo", "{}", "Unimplemented" },
        { "falla inesperada del proveedor", RpcConsultas.Multas, JsonFormatter.Default.Format(Multas("falso_explota")), "Internal" },
        { "el proveedor no responde dentro del tiempo máximo", RpcConsultas.Multas, JsonFormatter.Default.Format(Multas("falso_lento")), "DeadlineExceeded" },
    };

    [Theory]
    [MemberData(nameof(Errores))]
    public async Task Un_error_es_un_resultado_ERROR_con_su_grpcStatus_sin_respuesta_ni_mensaje(string caso, string rpc, string solicitud, string grpcStatus)
    {
        var sobre = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.SimitPropietario, rpc, solicitud);

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var resultado = (await bus.ResultadosAsync(sobre)).Should().ContainSingle(caso).Subject;
        var datos = resultado.DataAs<ResultadoConsultaDiagnostico>();
        (datos.Estado, datos.GrpcStatus, datos.Respuesta).Should().Be(("ERROR", grpcStatus, null), caso);
        resultado.Data.GetRawText().Should().NotContain("9999900011").And.NotContain("detalle", "nunca viaja el mensaje de la excepción");
    }

    [Fact]
    public async Task Una_falla_se_consulta_una_sola_vez_los_reintentos_son_de_Diagnostico()
    {
        var antes = bus.Llamadas.De("falso_explota");
        var sobre = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.SimitPropietario, RpcConsultas.Multas, Multas("falso_explota"));

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        (bus.Llamadas.De("falso_explota") - antes).Should().Be(1);
    }

    [Fact]
    public async Task SIMIT_usa_el_proveedor_que_configura_la_empresa_del_sobre()
    {
        var interna = Guid.NewGuid();
        var externa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(interna, "internal");
        await bus.SembrarEmpresaAsync(externa, "external");
        var deInterna = AnfitrionBus.Trabajo(interna, FuentesBus.SimitPropietario, RpcConsultas.Multas, Multas(proveedor: null));
        var deExterna = AnfitrionBus.Trabajo(externa, FuentesBus.SimitPropietario, RpcConsultas.Multas, Multas(proveedor: null));

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(deInterna);
        await bus.EjecutarAsync<ConsumidorTrabajosLote>(deExterna);

        Respondio(await bus.ResultadosAsync(deInterna)).Should().Be(FinesProviderResolver.FlitFines);
        Respondio(await bus.ResultadosAsync(deExterna)).Should().Be(FinesProviderResolver.VerifikSimit);
        (await bus.ConsumosAsync(interna)).Should().ContainSingle().Which.Should().BeEquivalentTo(new { Producto = "diagnostico", Fuente = "multas", Proveedor = FinesProviderResolver.FlitFines });
        (await bus.ConsumosAsync(externa)).Should().ContainSingle().Which.Proveedor.Should().Be(FinesProviderResolver.VerifikSimit);
    }

    [Fact]
    public async Task RUNT_persona_usa_la_cadena_de_la_empresa_y_publica_la_respuesta_completa()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.RuntPropietario, RpcConsultas.Conductor,
            new ConsultarConductorRequest { Opciones = new OpcionesConsulta(), Documento = new DocumentoIdentidad { Tipo = "CC", Numero = "9999900011" } });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var resultado = (await bus.ResultadosAsync(sobre)).Single();
        Respondio([resultado]).Should().Be("falso_conductor");
        resultado.Data.GetRawText().Should().Contain("NOMBRE FICTICIO PRUEBA", "HU #14087 AC8: person_full_name viaja (sin minimización)");
        (await bus.ConsumosAsync(empresa)).Should().ContainSingle().Which.Should().BeEquivalentTo(new { Producto = "diagnostico", Fuente = "conductor" });
    }

    [Fact]
    public async Task RUES_y_avaluos_se_atienden_por_su_rpc()
    {
        var empresa = Guid.NewGuid();
        var rues = AnfitrionBus.Trabajo(empresa, FuentesBus.Rues, RpcConsultas.Rues, new ConsultarRuesRequest { Opciones = new OpcionesConsulta(), Nit = "999990002" });
        var avaluo = AnfitrionBus.Trabajo(empresa, FuentesBus.Avaluo, RpcConsultas.Avaluos,
            new ConsultarAvaluosRequest { Opciones = new OpcionesConsulta(), Placa = new Placa { Valor = "TST401" }, Modelo = 2020, Cilindraje = 1600 });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(rues);
        await bus.EjecutarAsync<ConsumidorTrabajosLote>(avaluo);

        Respondio(await bus.ResultadosAsync(rues)).Should().Be("verifik_rues");
        var datosAvaluo = (await bus.ResultadosAsync(avaluo)).Single().DataAs<ResultadoConsultaDiagnostico>();
        datosAvaluo.Estado.Should().Be("OK");
        JsonParser.Default.Parse<ConsultarAvaluosResponse>(datosAvaluo.Respuesta!.Value.GetRawText()).Avaluos.Should().NotBeEmpty();
        (await bus.ConsumosAsync(empresa)).Select(c => c.Fuente).Should().BeEquivalentTo("rues", "avaluos");
    }

    private static ConsultarMultasRequest Multas(string? proveedor) => new()
    {
        Opciones = new OpcionesConsulta { Proveedor = proveedor ?? string.Empty },
        Documento = new DocumentoIdentidad { Tipo = "CC", Numero = "9999900011" },
        TipoPersona = TipoPersona.Natural,
    };

    /// <summary>
    /// El proveedor falso que respondió: <c>resultado.proveedor</c> de la respuesta publicada; desde HU #14087 AC8 la respuesta va
    /// completa, así que la clave «respondio» que también siembra el falso viaja y dice lo mismo.
    /// </summary>
    private static string Respondio(IReadOnlyList<EventEnvelope> resultados)
    {
        var datos = resultados.Should().ContainSingle().Subject.DataAs<ResultadoConsultaDiagnostico>();
        datos.Estado.Should().Be("OK");
        var json = datos.Respuesta!.Value.GetRawText();
        var resultado = datos.Fuente == FuentesBus.Rues
            ? JsonParser.Default.Parse<ConsultarRuesResponse>(json).Resultado
            : datos.Fuente == FuentesBus.RuntPropietario
                ? JsonParser.Default.Parse<ConsultarConductorResponse>(json).Resultado
                : JsonParser.Default.Parse<ConsultarMultasResponse>(json).Resultado;
        resultado.Campos.Should().Contain(c => c.Clave == "respondio" && c.ValorTexto == resultado.Proveedor, "la respuesta va completa");
        return resultado.Proveedor;
    }
}
