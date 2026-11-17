using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 y #13737 (ADR-0075, T9, Q2, Q4) — <see cref="ConsumidorResultados"/>: cada <c>consultas.resultado.diagnostico</c>
/// fija la empresa del sobre, se correlaciona con la celda por <c>trabajo_id</c> (uno que ya no es el vigente se descarta sin
/// efecto), lee el <c>*Response</c> proto-JSON o el <c>grpcStatus</c>, lo aplica con el mapeo de la fuente y cierra la celda en la
/// transacción del mensaje (con su intento, los datos del vehículo y la liberación o el bloqueo de las demás celdas si es la
/// RUNT). Las celdas de persona esperan la respuesta de cada persona; un transitorio con reintentos se reintenta con un trabajo
/// nuevo y, agotados, queda en ERROR. El contrato de vuelta coincide con el fixture compartido. PostgreSQL real
/// (<see cref="ApiDePrueba"/>) con el bus de prueba (<see cref="BusDePrueba"/>); placas TST### y documentos 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ResultadosTests
/// </code>
/// </example>
public sealed class ResultadosTests
{
    // Contrato de vuelta (Q2): el fixture compartido se lee como ResultadoConsultaDiagnostico y su respuesta como el *Response de la RUNT.
    [Fact]
    public void El_fixture_del_resultado_se_lee_con_su_respuesta_proto_y_su_duracion()
    {
        var sobre = EventEnvelope.FromJson(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bus", "resultado-consulta-diagnostico.v1.json")));
        sobre.Type.Should().Be(ResultadoConsultaDiagnostico.Tipo);
        sobre.Producer.Should().Be(OpcionesBus.Productor);

        var data = sobre.DataAs<ResultadoConsultaDiagnostico>();
        var respuesta = data.Leer(sobre.OccurredAt);

        (data.Fuente, data.Estado, data.GrpcStatus, data.PersonaId, data.DesdeCache).Should().Be(("runt", "OK", (string?)null, (Guid?)null, false));
        respuesta.Fin.Should().Be(sobre.OccurredAt);
        respuesta.Inicio.Should().Be(sobre.OccurredAt - TimeSpan.FromMilliseconds(data.DuracionMs));
        var runt = MapeoRunt.Mapear(respuesta.Respuesta.Should().BeOfType<ConsultarVehiculoIntempoResponse>().Subject);
        (runt.Vehiculo!.Placa, runt.Vehiculo.Modelo).Should().Be(("TST001", 2020));
        var claves = JsonSerializer.SerializeToElement(data, EventEnvelope.JsonOptions).EnumerateObject().Select(p => p.Name);
        claves.Should().BeEquivalentTo(sobre.Data.EnumerateObject().Select(p => p.Name), "el record serializa las mismas claves del contrato");
    }

    [Theory]
    [InlineData("Unavailable", StatusCode.Unavailable)]
    [InlineData("DEADLINE_EXCEEDED", StatusCode.DeadlineExceeded)]
    [InlineData("failedPrecondition", StatusCode.FailedPrecondition)]
    [InlineData("NoExiste", StatusCode.Unknown)]
    [InlineData(null, StatusCode.Unknown)]
    public void El_grpcStatus_se_lee_por_su_nombre(string? grpcStatus, StatusCode esperado) =>
        ResultadoConsultaDiagnostico.Codigo(grpcStatus).Should().Be(esperado);

    // OK: la RUNT cierra COMPLETADO con los datos del vehículo y su intento (fin = occurredAt del resultado), queda en la bandeja y
    // libera las demás celdas del vehículo publicando su trabajo.
    [Fact]
    public async Task Resultado_OK_cierra_la_celda_y_libera_las_demas_publicandolas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        var bus = Motor.Bus(api);

        (await bus.ProcesarLoteAsync(ct)).Should().Be(1, "al principio solo está publicada la RUNT");

        var celdas = await Motor.CeldasAsync(api, empresa.Id, lote, ct);
        var runt = celdas.Single(c => c.Fuente == Fuente.Runt);
        var simit = celdas.Single(c => c.Fuente == Fuente.SimitVehiculo);
        (runt.Estado, runt.TrabajoId).Should().Be((EstadoCelda.Completado, bus.Atendidos.Single().TrabajoId));
        (simit.Estado, simit.VersionTablero).Should().Be((EstadoCelda.EnCurso, runt.VersionTablero), "la liberada se publica en la transacción que cierra la RUNT");
        (await OutboxDePrueba.TrabajosAsync(api, ct)).Should().ContainSingle(t => t.ConsultaId == simit.Id)
            .Which.TrabajoId.Should().Be(simit.TrabajoId!.Value);
        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, lote, ct)).Single();
        (vehiculo.Placa, vehiculo.Servicio).Should().Be(("TST001", "PARTICULAR"));
        var intento = (await MotorPersonas.IntentosAsync(empresa, runt.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.IdentificadorConsultado, intento.Servicio).Should().Be((ResultadoIntento.Success, "TST001", (ServicioConsulta?)ServicioConsulta.Intempo));
        (await Motor.EscalarAsync(api, $"SELECT count(*) FROM diagnostico.inbox WHERE consumer = '{OpcionesBus.Cola}'", ct)).Should().Be(1);
    }

    // ERROR definitivo: la RUNT queda en ERROR con su tipo y las demás celdas del vehículo BLOQUEADA_POR_RUNT, sin publicar nada.
    [Fact]
    public async Task Resultado_ERROR_definitivo_cierra_en_ERROR_y_bloquea_las_demas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        var bus = Motor.Bus(api, maxReintentos: 3);
        bus.Responder = _ => RespuestaSimulada.Fallo(StatusCode.InvalidArgument);

        await bus.ProcesarTodoAsync(ct);

        var celdas = await Motor.CeldasAsync(api, empresa.Id, lote, ct);
        celdas.Single(c => c.Fuente == Fuente.Runt).Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = "ESTADO_NO_OK", IntentosAuto = 0 });
        celdas.Single(c => c.Fuente == Fuente.SimitVehiculo).Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = OrquestadorVehiculo.BloqueadaPorRunt });
        bus.Atendidos.Should().ContainSingle("ni reintento ni trabajo de la bloqueada");
        bus.Demorados.Should().Be(0);
    }

    // Una respuesta OK que no se puede leer como el *Response de la fuente es RESPUESTA_INVALIDA (definitivo).
    [Fact]
    public async Task Respuesta_ilegible_es_RESPUESTA_INVALIDA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        bus.Responder = _ => new RespuestaSimulada(null);

        await bus.ProcesarTodoAsync(ct);

        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = "RESPUESTA_INVALIDA" });
    }

    // Correlación por trabajo_id: el resultado de un trabajo que ya no es el vigente (y uno con otra empresa en el sobre) se
    // confirma sin tocar la celda; el mismo eventId dos veces no repite el efecto.
    [Fact]
    public async Task Resultado_de_otro_trabajo_o_de_otra_empresa_se_descarta_y_el_repetido_no_repite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await MotorRunt.LoteAsync(api, empresas.A, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        var trabajo = (await bus.DescartarTrabajosAsync(ct)).Single();
        var ok = await ConsultasSimuladas.ResponderAsync(api, trabajo, ct);

        var viejo = bus.Resultado(trabajo with { Sobre = Con(trabajo.Sobre, "trabajoId", Guid.CreateVersion7()) }, ok);
        var ajeno = bus.Resultado(trabajo with { Sobre = trabajo.Sobre with { TenantId = empresas.B.Id } }, ok);
        (await bus.EntregarAsync(viejo, ct)).Should().BeTrue("se confirma sin efecto");
        (await bus.EntregarAsync(ajeno, ct)).Should().BeTrue("la celda de otra empresa no se ve");
        var celda = (await Motor.CeldasAsync(api, empresas.A.Id, lote, ct)).Single();
        (celda.Estado, celda.TrabajoId).Should().Be((EstadoCelda.EnCurso, trabajo.TrabajoId));

        var vigente = bus.Resultado(trabajo, ok);
        (await bus.EntregarAsync(vigente, ct)).Should().BeTrue();
        var cerrada = (await Motor.CeldasAsync(api, empresas.A.Id, lote, ct)).Single();
        cerrada.Estado.Should().Be(EstadoCelda.Completado);
        (await bus.EntregarAsync(vigente, ct)).Should().BeTrue("la bandeja confirma el repetido");
        (await Motor.CeldasAsync(api, empresas.A.Id, lote, ct)).Single().VersionTablero.Should().Be(cerrada.VersionTablero, "el repetido no vuelve a cerrar");
        (await MotorPersonas.IntentosAsync(empresas.A, cerrada.Id, ct)).Should().ContainSingle();
    }

    // Celda de persona: un trabajo por persona con el mismo trabajo_id; cada respuesta queda en el payload con su intento y la
    // celda sigue EN_CURSO hasta la última, que la cierra.
    [Fact]
    public async Task Celda_de_persona_espera_la_respuesta_de_cada_persona()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorPersonas.ArrancarAsync(null, b =>
        {
            b.Services.AddSingleton<IFuenteRuntPersona, FuenteRuntPersonaSimulada>();
            b.Services.AddSingleton<IFuenteRues, FuenteRuesSimulada>();
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301")], ct);
        var bus = Motor.Bus(api);
        (await bus.ProcesarLoteAsync(ct)).Should().Be(1, "primero la RUNT");
        var trabajos = await bus.DescartarTrabajosAsync(ct);
        trabajos.Should().HaveCount(3, "TST301 tiene dos personas naturales y una con NIT");
        trabajos.Select(t => t.TrabajoId).Distinct().Should().ContainSingle("todas las personas van con el mismo trabajo_id");
        trabajos.Select(t => t.Fuente).Should().BeEquivalentTo(["runt_propietario", "runt_propietario", "rues"]);

        for (var i = 0; i < trabajos.Count; i++)
        {
            await bus.EntregarAsync(bus.Resultado(trabajos[i], await ConsultasSimuladas.ResponderAsync(api, trabajos[i], ct)), ct);
            var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
            var esperado = i < trabajos.Count - 1 ? EstadoCelda.EnCurso : EstadoCelda.Completado;
            celda.Estado.Should().Be(esperado, $"tras {i + 1} de {trabajos.Count} respuestas");
            MotorPersonas.PersonasDelPayload(celda.Payload).Should().HaveCount(i + 1);
            (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().HaveCount(i + 1).And.OnlyContain(x => x.PersonaId != null && x.IdentificadorConsultado == null);
        }
    }

    // Q4 (U2 = a, HU #13737): un transitorio con MaxReintentos=1 deja la celda PENDIENTE y el resultado en la cola de reintentos;
    // al vencer la espera se publica un trabajo nuevo y, si vuelve a fallar, la celda cierra ERROR con su tipo.
    [Fact]
    public async Task Transitorio_se_reintenta_con_un_trabajo_nuevo_y_agotado_queda_en_ERROR()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api, maxReintentos: 1);
        bus.Responder = _ => RespuestaSimulada.Fallo(StatusCode.Unavailable);

        (await bus.ProcesarLoteAsync(ct)).Should().Be(1);
        var pendiente = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        (pendiente.Estado, pendiente.IntentosAuto, pendiente.TrabajoId).Should().Be((EstadoCelda.Pendiente, 1, bus.Atendidos.Single().TrabajoId));
        bus.Demorados.Should().Be(1, "el resultado espera en la cola de reintentos");
        (await bus.ProcesarLoteAsync(ct)).Should().Be(0, "antes de la espera no se publica nada");

        reloj.Avanzar(ProgramadorReintentos.Esperas[0]);
        (await bus.ProcesarLoteAsync(ct)).Should().Be(1, "vencida la espera, sale un trabajo nuevo");

        bus.Atendidos.Select(t => t.TrabajoId).Should().OnlyHaveUniqueItems().And.HaveCount(2);
        var cerrada = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        cerrada.Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = "RED", IntentosAuto = 1 });
        (await MotorPersonas.IntentosAsync(empresa, cerrada.Id, ct)).Select(i => i.TipoError).Should().Equal("RED", "RED");
        bus.Demorados.Should().Be(0);
    }

    /// <summary>El sobre con <paramref name="clave"/> de <c>data</c> cambiada.</summary>
    private static EventEnvelope Con(EventEnvelope sobre, string clave, Guid valor)
    {
        var data = System.Text.Json.Nodes.JsonNode.Parse(sobre.Data.GetRawText())!.AsObject();
        data[clave] = valor;
        return sobre with { Data = JsonSerializer.SerializeToElement(data) };
    }
}
