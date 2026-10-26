using System.Collections.Concurrent;
using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13743 (ADR-0075, brecha 2, Q8) — <c>diagnostico.lote.terminado</c>: cuando el cierre de una celda deja el lote en
/// COMPLETADO (<see cref="RecalculadorEstado.TrasCerrar"/>), la misma transacción encola en la outbox un evento con el id, el
/// estado, la versión del tablero, la fecha de cierre y los totales por estado de celda y por tipo de error, sin placas ni
/// documentos. Un lote que sigue EN_PROCESO no emite; si un reproceso lo reabre y se vuelve a cerrar, emite otra vez con una
/// <c>version</c> mayor (los consumidores deduplican por <c>(diagnosticoId, version)</c>). PostgreSQL real; RUNT simulado de
/// Intempo (TST##1 natural, TST##2 NIT, TST##8 respuesta inválida).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~LoteTerminadoTests
/// </code>
/// </example>
public sealed class LoteTerminadoTests
{
    // Al cerrar la última celda: un evento con los totales (2 vehículos × RUNT y SIMIT_VEHICULO completados; SIMIT_LOCATARIO en
    // NA porque ninguno tiene locatario), en flit.diagnostico y con la empresa del lote.
    [Fact]
    public async Task Al_cerrar_la_ultima_celda_publica_el_evento_con_los_totales()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorNa.ArrancarAsync(b => MotorNa.Espias(b, orden, Fuente.SimitVehiculo));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST002")], [Fuente.Runt, Fuente.SimitVehiculo], null, [Fuente.SimitLocatario], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Completado);
        var evento = (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().ContainSingle().Subject;
        (evento.Sobre.Type, evento.Sobre.Producer, evento.Sobre.TenantId, evento.Sobre.Version).Should().Be((LoteTerminado.Tipo, "diagnostico", empresa.Id, 1));
        var data = evento.Data;
        data.GetProperty("diagnosticoId").GetGuid().Should().Be(id);
        data.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        data.GetProperty("esExpress").GetBoolean().Should().BeFalse();
        data.GetProperty("version").GetInt64().Should().Be(cabecera.VersionTablero);
        data.GetProperty("terminadoEn").GetDateTimeOffset().Should().Be(cabecera.CerradoEn!.Value);
        data.GetProperty("vehiculos").GetInt32().Should().Be(2);
        Conteos(data.GetProperty("celdas")).Should().Equal(new Dictionary<string, int> { ["COMPLETADO"] = 4, ["NA"] = 2 });
        Conteos(data.GetProperty("erroresPorTipo")).Should().BeEmpty();
        foreach (var dato in new[] { "TST001", "TST002", "9999900001", "999990002" })
            evento.Payload.Should().NotContain(dato, "el evento no lleva placas ni documentos");
    }

    // El lote sigue EN_PROCESO (la RUNT de TST008 y su bloqueada quedan en ERROR, sin cierre del panel): ningún evento.
    [Fact]
    public async Task Lote_que_sigue_en_proceso_no_publica()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorNa.ArrancarAsync(b => MotorNa.Espias(b, orden, Fuente.SimitVehiculo));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST008")], [Fuente.Runt, Fuente.SimitVehiculo], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).Estado.Should().Be(EstadoDiagnostico.EnProceso);
        (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().BeEmpty();
    }

    // Reproceso y nuevo cierre: un segundo evento con una version mayor. El primer cierre es el normal (COMPLETADO); para reabrir
    // hace falta una celda en ERROR, así que se deja el lote como lo dejaría un cierre del panel (INCOMPLETO con su celda en ERROR).
    [Fact]
    public async Task Reproceso_y_nuevo_cierre_publica_otro_evento_con_version_mayor()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorNa.ArrancarAsync(b => MotorNa.Espias(b, orden, Fuente.SimitVehiculo));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [Fuente.Runt, Fuente.SimitVehiculo], null, null, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        var primero = (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().ContainSingle().Subject;
        var simit = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.SimitVehiculo);
        await Motor.EjecutarAsync(api, $"""
            UPDATE diagnostico.consultas SET estado = 'ERROR', tipo_error = 'TIMEOUT', payload = NULL, origen = NULL, dato_obtenido_en = NULL WHERE id = '{simit.Id}';
            UPDATE diagnostico.diagnosticos SET estado = 'INCOMPLETO' WHERE id = '{id}';
            """, ct);
        var admin = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]);

        var reproceso = await api.Cliente(admin).PostAsync($"{Program.Prefijo}/consultas/{simit.Id}/reprocesar", null, ct);
        reproceso.StatusCode.Should().Be(HttpStatusCode.Accepted, await reproceso.Content.ReadAsStringAsync(ct));
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var eventos = await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct);
        eventos.Should().HaveCount(2);
        var segundo = eventos[1];
        segundo.Data.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        segundo.Data.GetProperty("version").GetInt64().Should().BeGreaterThan(primero.Data.GetProperty("version").GetInt64());
        segundo.Sobre.EventId.Should().NotBe(primero.Sobre.EventId);
        segundo.Data.GetProperty("diagnosticoId").GetGuid().Should().Be(id);
    }

    private static Dictionary<string, int> Conteos(System.Text.Json.JsonElement objeto) =>
        objeto.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
}
