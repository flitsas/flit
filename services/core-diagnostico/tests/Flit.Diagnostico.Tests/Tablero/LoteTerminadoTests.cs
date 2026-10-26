using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13743 (ADR-0075, brecha 2, Q8) — <c>diagnostico.lote.terminado</c>: en los tres caminos que cierran el lote (el cierre
/// automático de una celda, <see cref="RecalculadorEstado.TrasCerrar"/>; el cierre del panel, INCOMPLETO; y el llenado manual de la
/// última celda, COMPLETADO) la misma transacción encola en la outbox UN evento por transición con el id, el estado, la versión del
/// tablero, la fecha de cierre y los totales por estado de celda y por tipo de error, sin placas ni documentos. Un lote que sigue
/// EN_PROCESO no emite; si un reproceso lo reabre y se vuelve a cerrar, emite otra vez con una <c>version</c> mayor (los
/// consumidores deduplican por <c>(diagnosticoId, version)</c>). PostgreSQL real; RUNT simulado de Intempo (TST##1 natural,
/// TST##2 NIT, TST##8 respuesta inválida).
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

    // Reproceso y nuevo cierre: un segundo evento con una version mayor. El SIMIT falla la primera vez (definitivo), el panel cierra
    // el lote en INCOMPLETO (primer evento), el reproceso lo reabre y el cierre automático lo deja COMPLETADO (segundo evento).
    [Fact]
    public async Task Reproceso_y_nuevo_cierre_publica_otro_evento_con_version_mayor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync(b => b.Services.AddSingleton<IEjecutorCelda>(new SimitQueFallaUnaVez()));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [Fuente.Runt, Fuente.SimitVehiculo], null, null, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);
        (await CerrarPanelAsync(api, empresa, id, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var primero = (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().ContainSingle().Subject;
        primero.Data.GetProperty("estado").GetString().Should().Be("INCOMPLETO");
        var simit = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.SimitVehiculo);
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

    // Cierre del panel (TST008: RUNT en ERROR y su SIMIT bloqueada): un evento INCOMPLETO con los totales por estado y por tipo de
    // error, la versión del tablero y la fecha de cierre del lote; volver a cerrar (409) no emite otro.
    [Fact]
    public async Task Cerrar_el_panel_publica_el_evento_INCOMPLETO_con_los_totales()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorNa.ArrancarAsync(b => MotorNa.Espias(b, orden, Fuente.SimitVehiculo));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST008")], [Fuente.Runt, Fuente.SimitVehiculo], null, null, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);
        (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().BeEmpty("con errores el lote sigue EN_PROCESO hasta el panel");

        (await CerrarPanelAsync(api, empresa, id, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CerrarPanelAsync(api, empresa, id, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Incompleto);
        var evento = (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().ContainSingle("una sola transición, un solo evento").Subject;
        evento.Sobre.TenantId.Should().Be(empresa.Id);
        var data = evento.Data;
        data.GetProperty("estado").GetString().Should().Be("INCOMPLETO");
        data.GetProperty("version").GetInt64().Should().Be(cabecera.VersionTablero);
        data.GetProperty("terminadoEn").GetDateTimeOffset().Should().Be(cabecera.CerradoEn!.Value);
        data.GetProperty("vehiculos").GetInt32().Should().Be(2);
        Conteos(data.GetProperty("celdas")).Should().Equal(new Dictionary<string, int> { ["COMPLETADO"] = 2, ["ERROR"] = 2 });
        Conteos(data.GetProperty("erroresPorTipo")).Should().Equal(new Dictionary<string, int> { ["BLOQUEADA_POR_RUNT"] = 1, ["RESPUESTA_INVALIDA"] = 1 });
        evento.Payload.Should().NotContain("TST008");
    }

    // Llenado manual desde el backoffice de la última celda en ERROR: el lote pasa a COMPLETADO y emite un evento, con la empresa
    // de la celda (no la de FLIT) y la versión del tablero del llenado.
    [Fact]
    public async Task Llenar_a_mano_la_ultima_celda_publica_el_evento_COMPLETADO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 4, vehiculos:
            [new("TST731", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)])]);
        var celda = lote.Celda("TST731", Fuente.SimitVehiculo);
        var token = api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer, Permisos.ConsultasLlenar], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

        var respuesta = await api.Cliente(token).PutAsync($"{Program.Prefijo}/backoffice/consultas/{celda.Id}/manual?empresa={empresa.Id}",
            new StringContent($$"""{"version":2,"resultado":{{SimitManual}}}""", Encoding.UTF8, "application/json"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Completado);
        var evento = (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().ContainSingle().Subject;
        evento.Sobre.TenantId.Should().Be(empresa.Id, "el evento es de la compañía del lote");
        evento.Data.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        evento.Data.GetProperty("version").GetInt64().Should().Be(cabecera.VersionTablero);
        evento.Data.GetProperty("vehiculos").GetInt32().Should().Be(1);
        Conteos(evento.Data.GetProperty("celdas")).Should().Equal(new Dictionary<string, int> { ["COMPLETADO"] = 2 });
    }

    // Una transición, un evento: si el recálculo corre dos veces en la misma transacción con la misma versión, el lote terminado
    // se encola una sola vez.
    [Fact]
    public async Task Dos_recalculos_de_la_misma_transicion_encolan_un_solo_evento()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 9, vehiculos: [new("TST741", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        await using (var scope = OutboxDePrueba.Scope(api, empresa.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
            var outbox = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>();
            var ahora = DateTimeOffset.UtcNow;
            (await RecalculadorEstado.RecalcularAsync(db, outbox, lote.Id, 9, ahora, ct)).Should().NotBeNull();
            await RecalculadorEstado.RecalcularAsync(db, outbox, lote.Id, 9, ahora, ct);
            await db.SaveChangesAsync(ct);
        }

        (await OutboxDePrueba.EventosAsync(api, LoteTerminado.Tipo, ct)).Should().ContainSingle();
    }

    private const string SimitManual =
        """{"semaforo":"AMARILLO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0001","fecha":"2026-09-30","valor":"468500","estado":"PENDIENTE","organismo":"Organismo de prueba"}]}""";

    private static Task<HttpResponseMessage> CerrarPanelAsync(ApiDePrueba api, EmpresaDePrueba empresa, Guid id, CancellationToken ct) =>
        api.Cliente(api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]))
            .PostAsync(Tablero.RutaCierrePanel(id), null, ct);

    private static Dictionary<string, int> Conteos(System.Text.Json.JsonElement objeto) =>
        objeto.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

    /// <summary>Ejecutor SIMIT_VEHICULO que la primera vez falla con un error definitivo y después completa.</summary>
    private sealed class SimitQueFallaUnaVez : IEjecutorCelda
    {
        private int _llamadas;

        public Fuente Fuente => Fuente.SimitVehiculo;

        public Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct) =>
            Task.FromResult(Interlocked.Increment(ref _llamadas) == 1
                ? ResultadoCelda.Error("RESPUESTA_INVALIDA")
                : ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow));
    }
}
