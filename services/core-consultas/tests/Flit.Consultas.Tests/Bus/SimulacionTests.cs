using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Simulacion;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #13727 (ADR-0075, U3 = a; T10) — la simulación de LOCAL y DEV vive en core-consultas: un proveedor simulado por fuente,
/// determinista por el último carácter del identificador (los mismos escenarios que tenían las fuentes simuladas de
/// core-diagnostico), con los fixtures proto de Intempo. Solo se admite con <c>Consultas:Ambiente</c> LOCAL o DEV: en otro
/// ambiente el servicio no arranca. Datos inventados (TST…, 99999…).
/// <para>Uso de ejemplo: <c>new SimuladorConsultas().Responder(RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST003" })</c>.</para>
/// </summary>
public sealed class SimulacionTests
{
    private readonly SimuladorConsultas _simulador = new(TimeProvider.System);

    [Theory]
    [InlineData("LOCAL")]
    [InlineData("DEV")]
    [InlineData(" dev ")]
    public void Habilitada_en_LOCAL_o_DEV(string ambiente) =>
        OpcionesSimulacion.Desde(Config(true, ambiente)).Habilitada.Should().BeTrue();

    [Theory]
    [InlineData("QA")]
    [InlineData("PDN")]
    [InlineData("")]
    [InlineData(null)]
    public void Habilitada_en_otro_ambiente_o_sin_ambiente_impide_arrancar_y_dice_que_quitar(string? ambiente) =>
        FluentActions.Invoking(() => OpcionesSimulacion.Desde(Config(true, ambiente)))
            .Should().Throw<InvalidOperationException>().WithMessage("*Consultas__Simulacion__Habilitada*LOCAL o DEV*");

    [Fact]
    public void Apagada_por_defecto_en_cualquier_ambiente() =>
        OpcionesSimulacion.Desde(Config(null, "PDN")).Habilitada.Should().BeFalse();

    [Fact]
    public void Los_fixtures_de_Intempo_son_proto_JSON_estricto_de_la_respuesta()
    {
        var fixtures = FixturesIntempo.Todos();

        fixtures.Keys.Should().BeEquivalentTo(
            "intempo-persona-natural.json", "intempo-nit.json", "intempo-leasing-locatario-c.json", "intempo-leasing-locatario-n.json",
            "intempo-servicio-publico.json", "intempo-departamento-no-soportado.json", "intempo-error-timeout.json", "intempo-error-respuesta-invalida.json");
        var lector = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(false));
        foreach (var json in fixtures.Values)
            FluentActions.Invoking(() => lector.Parse<ConsultarVehiculoIntempoResponse>(json)).Should().NotThrow();
    }

    [Theory]
    [InlineData("TST003", "999990003", ResultadoRuntIntempo.Encontrado)]
    [InlineData("TST002", "999990002", ResultadoRuntIntempo.Encontrado)]
    [InlineData("TSTXYZ", null, ResultadoRuntIntempo.Encontrado)]
    [InlineData("TST007", null, ResultadoRuntIntempo.Error)]
    public void RUNT_por_placa_responde_el_fixture_del_ultimo_caracter_con_la_placa_consultada(string placa, string? documento, ResultadoRuntIntempo resultado)
    {
        var r = (ConsultarVehiculoIntempoResponse)_simulador.Responder(RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = placa });

        r.Resultado.Should().Be(resultado);
        if (resultado != ResultadoRuntIntempo.Encontrado)
        {
            r.TipoError.Should().Be(TipoErrorFuente.Timeout);
            return;
        }

        (r.Vehiculo.Plate, r.Vehiculo.InformacionGeneral.NoPlaca).Should().Be((placa, placa));
        if (documento is not null)
            r.Vehiculo.PropietariosActuales.Should().Contain(p => p.NoDocumento == documento);
    }

    [Fact]
    public void RUNT_por_VIN_lleva_el_VIN_consultado()
    {
        var r = (ConsultarVehiculoIntempoResponse)_simulador.Responder(RpcConsultas.PorVin, new ConsultarPorVinRequest { Vin = "9ZZPRUEBA00000005" });

        (r.Resultado, r.Vehiculo.Vin, r.Vehiculo.InformacionGeneral.NoVin).Should().Be((ResultadoRuntIntempo.Encontrado, "9ZZPRUEBA00000005", "9ZZPRUEBA00000005"));
    }

    [Fact]
    public void RUNT_persona_inscrita_con_licencia_suspendida_tiempo_agotado_o_respuesta_invalida()
    {
        var activa = (ConsultarConductorResponse)_simulador.Responder(RpcConsultas.Conductor, Conductor("9999900011"));
        var suspendida = (ConsultarConductorResponse)_simulador.Responder(RpcConsultas.Conductor, Conductor("9999900013"));
        var invalida = (ConsultarConductorResponse)_simulador.Responder(RpcConsultas.Conductor, Conductor("9999900018"));

        activa.Resultado.Chequeos.Should().ContainSingle(c => c.Clave == "conductor_identidad" && c.Estado == EstadoChequeo.Ok);
        Campo(activa.Resultado, "person_license_status").Should().Be("ACTIVO");
        Campo(activa.Resultado, "person_license_categories").Should().Be("B1");
        Campo(suspendida.Resultado, "person_license_status").Should().Be("SUSPENDIDO");
        invalida.Resultado.Should().BeNull("Diagnóstico la traduce como RESPUESTA_INVALIDA");
        Codigo(() => _simulador.Responder(RpcConsultas.Conductor, Conductor("9999900017"))).Should().Be(StatusCode.DeadlineExceeded);
    }

    [Fact]
    public void SIMIT_semaforo_y_comparendos_por_placa_o_documento()
    {
        var verde = (ConsultarMultasResponse)_simulador.Responder(RpcConsultas.Multas, Multas(placa: "TST001"));
        var rojo = (ConsultarMultasResponse)_simulador.Responder(RpcConsultas.Multas, Multas(placa: "TST002"));
        var muchos = (ConsultarMultasResponse)_simulador.Responder(RpcConsultas.Multas, Multas(documento: "9999900016"));

        verde.Resultado.Semaforo.Should().Be(Semaforo.Verde);
        verde.Resultado.Chequeos.SelectMany(c => c.Comparendos).Should().BeEmpty();
        rojo.Resultado.Semaforo.Should().Be(Semaforo.Rojo);
        rojo.Resultado.Chequeos.SelectMany(c => c.Comparendos).Select(c => c.Numero).Should().Equal("SIM-0001", "SIM-0002");
        muchos.Resultado.Chequeos.SelectMany(c => c.Comparendos).Should().HaveCount(60);
        Codigo(() => _simulador.Responder(RpcConsultas.Multas, Multas(placa: "TST007"))).Should().Be(StatusCode.DeadlineExceeded);
    }

    [Fact]
    public void RUES_activa_o_cancelada_con_su_renovacion()
    {
        var activa = (ConsultarRuesResponse)_simulador.Responder(RpcConsultas.Rues, new ConsultarRuesRequest { Nit = "999990002" });
        var cancelada = (ConsultarRuesResponse)_simulador.Responder(RpcConsultas.Rues, new ConsultarRuesRequest { Nit = "999990006" });

        var m = activa.Resultado.Certificaciones.MatriculasMercantiles.Single();
        (m.Estado.Crudo, m.RenovadaEn.Valor).Should().Be(("ACTIVA", "2026-03-31"));
        cancelada.Resultado.Certificaciones.MatriculasMercantiles.Single().Estado.Crudo.Should().Be("CANCELADA");
        Codigo(() => _simulador.Responder(RpcConsultas.Rues, new ConsultarRuesRequest { Nit = "999990007" })).Should().Be(StatusCode.DeadlineExceeded);
    }

    [Fact]
    public void Avaluo_con_valor_sugerido_solo_fasecolda_o_sin_datos()
    {
        var completo = (ConsultarAvaluosResponse)_simulador.Responder(RpcConsultas.Avaluos, Avaluo("TST001"));
        var fasecolda = (ConsultarAvaluosResponse)_simulador.Responder(RpcConsultas.Avaluos, Avaluo("TST002"));
        var sinDatos = (ConsultarAvaluosResponse)_simulador.Responder(RpcConsultas.Avaluos, Avaluo("TST004"));

        (completo.ValorSugerido, completo.FuentePrincipal, completo.Avaluos.Count).Should().Be((52_000_000, "fasecolda", 3));
        completo.Avaluos.Single(a => a.Fuente == "mercado_libre").Muestras.Should().Be(9);
        (fasecolda.ValorSugerido, fasecolda.Avaluos.Count(a => a.Estado == EstadoAvaluo.Ok)).Should().Be((48_000_000, 1));
        (sinDatos.ValorSugerido, sinDatos.Avaluos.All(a => a.Estado == EstadoAvaluo.SinDatos)).Should().Be((0, true));
        Codigo(() => _simulador.Responder(RpcConsultas.Avaluos, Avaluo("TST007"))).Should().Be(StatusCode.DeadlineExceeded);
    }

    [Fact]
    public void Impuesto_con_deuda_o_pagado()
    {
        var conDeuda = (ConsultarImpuestoVehicularResponse)_simulador.Responder(RpcConsultas.Impuesto, Impuesto("TST001"));
        var pagado = (ConsultarImpuestoVehicularResponse)_simulador.Responder(RpcConsultas.Impuesto, Impuesto("TST010"));

        (conDeuda.MontoTotal, conDeuda.Vigencias.Count(v => !v.Pagada)).Should().Be((400_000, 2));
        (pagado.MontoTotal, pagado.Vigencias.All(v => v.Pagada)).Should().Be((0, true));
        Codigo(() => _simulador.Responder(RpcConsultas.Impuesto, Impuesto("TST007"))).Should().Be(StatusCode.DeadlineExceeded);
    }

    // HU #14087 AC8: sin minimización, cada respuesta simulada se publica entera (con los nombres ficticios de los fixtures) y
    // core-diagnostico la lee de vuelta igual.
    [Fact]
    public void Cada_respuesta_simulada_se_publica_completa_y_se_lee_de_vuelta_igual()
    {
        var solicitudes = new (string Rpc, IMessage Solicitud)[]
        {
            (RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST003" }),
            (RpcConsultas.PorVin, new ConsultarPorVinRequest { Vin = "9ZZPRUEBA00000004" }),
            (RpcConsultas.Conductor, Conductor("9999900011")),
            (RpcConsultas.Multas, Multas(placa: "TST002")),
            (RpcConsultas.Rues, new ConsultarRuesRequest { Nit = "999990002" }),
            (RpcConsultas.Avaluos, Avaluo("TST001")),
            (RpcConsultas.Impuesto, Impuesto("TST001")),
        };
        var publicador = new PublicadorResultado(new OutboxCapturada());
        var trabajo = new TrabajoConsultaDiagnostico(Guid.CreateVersion7(), Guid.CreateVersion7(), null, FuentesBus.Runt, RpcConsultas.PorPlaca,
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone());

        foreach (var (rpc, solicitud) in solicitudes)
        {
            var respuesta = _simulador.Responder(rpc, solicitud);
            var datos = publicador.Publicar(Guid.NewGuid(), trabajo with { Rpc = rpc }, new EjecucionTrabajo(respuesta, null, false, TimeSpan.Zero))
                .DataAs<ResultadoConsultaDiagnostico>();
            JsonParser.Default.Parse(datos.Respuesta!.Value.GetRawText(), respuesta.Descriptor).Should().Be(respuesta, rpc);
        }

        var intempo = publicador.Publicar(Guid.NewGuid(), trabajo, new EjecucionTrabajo(_simulador.Responder(RpcConsultas.PorPlaca, solicitudes[0].Solicitud), null, false, TimeSpan.Zero))
            .DataAs<ResultadoConsultaDiagnostico>().Respuesta!.Value.GetRawText();
        intempo.Should().Contain("FICTICI", "los nombres ficticios de los fixtures viajan: la respuesta va completa");
    }

    [Fact]
    public void Un_rpc_sin_simulacion_responde_UNIMPLEMENTED() =>
        Codigo(() => _simulador.Responder("ConsultasService/ConsultarVehiculo", new ConsultarVehiculoRequest())).Should().Be(StatusCode.Unimplemented);

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static IConfiguration Config(bool? habilitada, string? ambiente) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [OpcionesSimulacion.ClaveHabilitada] = habilitada?.ToString(),
        [OpcionesSimulacion.ClaveAmbiente] = ambiente,
    }).Build();

    private static StatusCode Codigo(Action accion)
    {
        try
        {
            accion();
        }
        catch (RpcException ex)
        {
            return ex.StatusCode;
        }

        return StatusCode.OK;
    }

    private static string? Campo(ResultadoConsulta r, string clave) => r.Campos.FirstOrDefault(c => c.Clave == clave)?.ValorTexto;

    private static ConsultarConductorRequest Conductor(string numero) => new() { Documento = new DocumentoIdentidad { Tipo = "CC", Numero = numero } };

    private static ConsultarMultasRequest Multas(string? placa = null, string? documento = null)
    {
        var r = new ConsultarMultasRequest { TipoPersona = TipoPersona.Natural };
        if (placa is not null)
            r.Placa = new Placa { Valor = placa };
        if (documento is not null)
            r.Documento = new DocumentoIdentidad { Tipo = "CC", Numero = documento };
        return r;
    }

    private static ConsultarAvaluosRequest Avaluo(string placa) => new() { Placa = new Placa { Valor = placa }, Modelo = 2020, Cilindraje = 1600 };

    private static ConsultarImpuestoVehicularRequest Impuesto(string placa) => new() { Placa = new Placa { Valor = placa }, DepartamentoCodigo = "05" };
}
