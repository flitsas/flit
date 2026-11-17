using System.Net;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU14231 pasó la caída de los proveedores de multas (<c>verifik_simit</c>, <c>flit_fines</c>, <c>kyverum_fines</c>) de chequeo
/// <c>provider</c> en ERROR a chequeo <c>provider</c> en WARN con semáforo amarillo, para que Trámites no se bloquee. Para Diagnóstico
/// sigue siendo un proveedor que no respondió: la celda queda en ERROR con el tipo que dicta el motivo, reprocesable y sin «0
/// comparendos». Un aviso que no es del proveedor (datos de entrada, multas) no es una caída. Puras salvo las que reciben la API
/// (PostgreSQL real); placas TST1## y documentos 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SimitCaidaEnAvisoTests
/// </code>
/// </example>
public sealed class SimitCaidaEnAvisoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Lo que hoy responde un proveedor de multas caído (<c>ProviderUnavailable</c> de HU14231).</summary>
    private static ConsultarMultasResponse CaidoEnAviso(string proveedor = "verifik_simit")
    {
        var resultado = new ResultadoConsulta { Proveedor = proveedor, Semaforo = Semaforo.Amarillo };
        resultado.Chequeos.Add(new Chequeo { Clave = "provider", Estado = EstadoChequeo.Warn });
        return new ConsultarMultasResponse { Resultado = resultado };
    }

    private static RespuestaFuente<ResultadoSimit> Mapear(IMessage respuesta, string? motivo) =>
        new RespuestaConsulta(null, FuentesBus.SimitVehiculo, respuesta, null, Ahora.AddSeconds(-1), Ahora, false, motivo)
            .Mapear<ConsultarMultasResponse, ResultadoSimit>(MapeoSimit.Mapear, MapeoSimit.Error);

    public static TheoryData<string, string, string, bool> CaidasPorProveedor
    {
        get
        {
            var datos = new TheoryData<string, string, string, bool>();
            foreach (var proveedor in new[] { "verifik_simit", "flit_fines", "kyverum_fines" })
            {
                datos.Add(proveedor, "CREDENCIALES", "AUTENTICACION", false);
                datos.Add(proveedor, "RECHAZO", "ESTADO_NO_OK", false);
                datos.Add(proveedor, "LIMITE", "LIMITE_PROVEEDOR", true);
                datos.Add(proveedor, "CAIDA", "HTTP_5XX", true);
                datos.Add(proveedor, "RED", "RED", true);
                datos.Add(proveedor, "TIMEOUT", "TIMEOUT", true);
            }

            return datos;
        }
    }

    [Theory]
    [MemberData(nameof(CaidasPorProveedor))]
    public void Chequeo_warn_de_caida_es_error_clasificado_por_el_motivo_y_sin_resultado(string proveedor, string motivo, string tipo, bool transitorio)
    {
        var r = Mapear(CaidoEnAviso(proveedor), motivo);

        r.Error.Should().NotBeNull($"{proveedor} {motivo}: el proveedor no respondió");
        (r.Error!.TipoError, r.Error.EsTransitorio).Should().Be((tipo, transitorio));
        r.Resultado.Should().BeNull("no hay «0 comparendos» de un proveedor caído");
        r.Servicio.Should().Be(TraductorErroresConsultas.Servicio(proveedor), "el intento conserva quién falló");
        new PoliticaReintentos(1).Decidir(r.Error.TipoError, 0, Ahora).Reintentar.Should().Be(transitorio);
    }

    // Un mensaje sin motivo con el proveedor caído (viejo o de un proveedor que no clasifica) es la misma caída: HTTP_5XX, no un
    // verde ni un amarillo sin comparendos.
    [Fact]
    public void Chequeo_warn_de_caida_sin_motivo_es_HTTP_5XX()
    {
        var r = Mapear(CaidoEnAviso(), null);

        (r.Error?.TipoError, r.Resultado).Should().Be(("HTTP_5XX", (ResultadoSimit?)null));
    }

    // Protección contra el arreglo excesivo: datos de entrada, un aviso de multas o una consulta sana sin comparendos no son caída.
    [Theory]
    [InlineData("input", EstadoChequeo.Unknown, Semaforo.Amarillo, "AMARILLO")]
    [InlineData("multas", EstadoChequeo.Warn, Semaforo.Amarillo, "AMARILLO")]
    [InlineData("multas", EstadoChequeo.Ok, Semaforo.Verde, "VERDE")]
    public void Sin_caida_del_proveedor_no_es_error(string clave, EstadoChequeo estado, Semaforo semaforo, string esperado)
    {
        var resultado = new ResultadoConsulta { Proveedor = "verifik_simit", Semaforo = semaforo };
        resultado.Chequeos.Add(new Chequeo { Clave = clave, Estado = estado });

        var r = Mapear(new ConsultarMultasResponse { Resultado = resultado }, null);

        r.Error.Should().BeNull();
        (r.Resultado!.Semaforo, r.Resultado.CantidadComparendos).Should().Be((esperado, 0));
    }

    // Celda real: credencial vencida sin reintento, sin payload, con su intento; reprocesable, y con el proveedor ya sano completa.
    [Fact]
    public async Task Celda_SIMIT_con_credencial_vencida_queda_en_ERROR_definitivo_y_reprocesable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<IFuenteRunt>(new RuntParaSimit()));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST102")], ct, Fuente.SimitVehiculo);
        var bus = Motor.Bus(api, maxReintentos: 3);
        bus.Responder = t => t.Fuente == FuentesBus.SimitVehiculo ? new RespuestaSimulada(CaidoEnAviso(), MotivoFallo: "CREDENCIALES") : null;

        await bus.ProcesarTodoAsync(ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST102", Fuente.SimitVehiculo, ct);
        (celda.Estado, celda.TipoError, celda.IntentosAuto, celda.Payload).Should().Be((EstadoCelda.Error, "AUTENTICACION", 0, (string?)null));
        var intento = (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.TipoError, intento.IdentificadorConsultado).Should().Be((ResultadoIntento.Error, "AUTENTICACION", "TST102"));

        var token = api.Token(empresa: empresa.Id, roles: ["admin_diagnostico"], permisos: [Permisos.DiagnosticosVer, Permisos.ConsultasReprocesar]);
        var reproceso = await api.Cliente(token).PostAsync($"{Program.Prefijo}/consultas/{celda.Id}/reprocesar", null, ct);
        reproceso.StatusCode.Should().Be(HttpStatusCode.Accepted, await reproceso.Content.ReadAsStringAsync(ct));
        var sano = new ResultadoConsulta { Proveedor = "verifik_simit", Semaforo = Semaforo.Verde };
        sano.Chequeos.Add(new Chequeo { Clave = "multas", Estado = EstadoChequeo.Ok });
        bus.Responder = t => t.Fuente == FuentesBus.SimitVehiculo ? RespuestaSimulada.Ok(new ConsultarMultasResponse { Resultado = sano }) : null;
        await bus.ProcesarTodoAsync(ct);

        (await MotorPersonas.CeldaAsync(api, empresa, lote, "TST102", Fuente.SimitVehiculo, ct)).Estado.Should().Be(EstadoCelda.Completado);
    }

    // Caída transitoria: primero se reintenta y, agotados los reintentos, queda en ERROR HTTP_5XX sin payload.
    [Fact]
    public async Task Celda_SIMIT_con_caida_se_reintenta_y_agotada_queda_en_ERROR()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            b.Services.AddSingleton<IFuenteRunt>(new RuntParaSimit());
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST103")], ct, Fuente.SimitVehiculo);
        var bus = Motor.Bus(api, maxReintentos: 1);
        bus.Responder = t => t.Fuente == FuentesBus.SimitVehiculo ? new RespuestaSimulada(CaidoEnAviso("flit_fines"), MotivoFallo: "CAIDA") : null;

        await bus.ProcesarTodoAsync(ct);

        var pendiente = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST103", Fuente.SimitVehiculo, ct);
        (pendiente.Estado, pendiente.IntentosAuto).Should().Be((EstadoCelda.Pendiente, 1), "un transitorio se reintenta");

        reloj.Avanzar(PoliticaReintentos.EsperaPorDefecto);
        await bus.ProcesarTodoAsync(ct);

        var cerrada = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST103", Fuente.SimitVehiculo, ct);
        (cerrada.Estado, cerrada.TipoError, cerrada.IntentosAuto, cerrada.Payload).Should().Be((EstadoCelda.Error, "HTTP_5XX", 1, (string?)null));
    }

    // Celda de persona: la misma caída deja a la persona en ERROR con su tipo, sin resultado ni comparendos.
    [Fact]
    public async Task Celda_SIMIT_de_persona_con_credencial_vencida_queda_en_ERROR_sin_resultado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<IFuenteRunt>(new RuntSimitPersonas()));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST404")], [Fuente.Runt], [Fuente.SimitPropietario], null, ct);
        var bus = Motor.Bus(api, maxReintentos: 3);
        bus.Responder = t => t.Fuente == FuentesBus.SimitPropietario ? new RespuestaSimulada(CaidoEnAviso("kyverum_fines"), MotivoFallo: "CREDENCIALES") : null;

        await bus.ProcesarTodoAsync(ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST404", Fuente.SimitPropietario, ct);
        (celda.Estado, celda.TipoError, celda.IntentosAuto).Should().Be((EstadoCelda.Error, "AUTENTICACION", 0));
        var persona = MotorPersonas.PersonasDelPayload(celda.Payload).Should().ContainSingle().Subject;
        (persona.GetProperty("estado").GetString(), persona.GetProperty("tipo_error").GetString()).Should().Be(("ERROR", "AUTENTICACION"));
        persona.TryGetProperty("resultado", out _).Should().BeFalse("sin «0 comparendos» de un proveedor caído");
    }

    // Celda real con un aviso que no es caída y sin motivo: completa, sin error, con su semáforo.
    [Fact]
    public async Task Celda_SIMIT_con_aviso_sin_caida_completa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<IFuenteRunt>(new RuntParaSimit()));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST104")], ct, Fuente.SimitVehiculo);
        var bus = Motor.Bus(api);
        var entrada = new ResultadoConsulta { Proveedor = "verifik_simit", Semaforo = Semaforo.Amarillo };
        entrada.Chequeos.Add(new Chequeo { Clave = "input", Estado = EstadoChequeo.Unknown });
        bus.Responder = t => t.Fuente == FuentesBus.SimitVehiculo ? RespuestaSimulada.Ok(new ConsultarMultasResponse { Resultado = entrada }) : null;

        await bus.ProcesarTodoAsync(ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST104", Fuente.SimitVehiculo, ct);
        (celda.Estado, celda.TipoError).Should().Be((EstadoCelda.Completado, (string?)null));
        using var payload = JsonDocument.Parse(celda.Payload!);
        payload.RootElement.GetProperty("semaforo").GetString().Should().Be("AMARILLO");
    }
}
