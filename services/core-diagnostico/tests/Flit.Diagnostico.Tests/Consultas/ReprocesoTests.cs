using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Tablero;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13746 (F03-H8, R-1, R-8) — <c>POST /consultas/{cid}/reprocesar</c>: el admin de la compañía devuelve a PENDIENTE una
/// celda en ERROR (<c>intentos_auto=0</c>, sube <c>version_tablero</c>) y el procesador repite la consulta real (sin la
/// reutilización del mismo día) con un intento <c>disparador=REPROCESO</c> y su usuario. Solo celdas en ERROR (otra: 409); el
/// usuario del producto recibe 403 y otra compañía 404. Una celda BLOQUEADA_POR_RUNT con la RUNT en ERROR responde 409; al
/// reprocesar la RUNT en OK, sus bloqueadas vuelven a PENDIENTE en la misma transacción y corren con el usuario del reproceso.
/// Un lote cerrado vuelve a EN_PROCESO con historial REPROCESO y el recalculador fija el estado final. PostgreSQL real; placas
/// del simulador: TST##1 natural, TST##8 respuesta inválida (error definitivo).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Consultas.ReprocesoTests
/// </code>
/// </example>
public sealed class ReprocesoTests
{
    // AC1 (F03-C14) — RUNT_PROPIETARIO en ERROR: se repite la consulta real (aunque haya un dato de hoy reutilizable), el intento
    // queda con REPROCESO y el usuario, la celda sube la versión del tablero y las COMPLETADO del vehículo no cambian.
    [Fact]
    public async Task Admin_reprocesa_celda_ERROR_y_queda_intento_REPROCESO_con_usuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b => new EjecutorFuenteDePrueba(Fuente.RuntPropietario, orden).Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 5, vehiculos:
            [new("TST201", [new(Fuente.Runt, EstadoCelda.Completado, 2), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 3), new(Fuente.RuntPropietario, EstadoCelda.Error, 4)])]);
        // Otro lote de hoy con el mismo dato: el reproceso no lo reutiliza, repite la consulta real.
        await TableroAyudas.SembrarAsync(empresa, ct, nombre: "Otro lote de hoy", vehiculos:
            [new("TST201", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.RuntPropietario, EstadoCelda.Completado, 1)])]);
        var celda = lote.Celda("TST201", Fuente.RuntPropietario);
        var completadasAntes = (await CeldasAsync(empresa, lote.Id, ct)).Where(c => c.Estado == EstadoCelda.Completado).OrderBy(c => c.Fuente).ToList();
        var token = Admin(api, empresa);

        var respuesta = await api.Cliente(token).PostAsync(Ruta(celda.Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await TableroAyudas.JsonAsync(respuesta, ct);
        cuerpo.GetProperty("id").GetGuid().Should().Be(celda.Id);
        cuerpo.GetProperty("estado").GetString().Should().Be("PENDIENTE");
        cuerpo.GetProperty("estado_lote").GetString().Should().Be("EN_PROCESO");
        var enCola = await CeldaAsync(empresa, celda.Id, ct);
        (enCola.Estado, enCola.IntentosAuto, enCola.TipoError).Should().Be((EstadoCelda.Pendiente, 0, (string?)null));
        enCola.VersionTablero.Should().Be(6, "el reproceso sube la versión del tablero y la celda vuelve a gris en el sondeo");
        (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).VersionTablero.Should().Be(6);
        cuerpo.GetProperty("version").GetInt64().Should().Be(6);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        orden.Should().Equal([$"RUNT_PROPIETARIO {celda.VehiculoId}"], "se repite la consulta real una vez, sin reutilizar el dato de hoy");
        (await CeldaAsync(empresa, celda.Id, ct)).Estado.Should().Be(EstadoCelda.Completado);
        var intento = (await IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Disparador, intento.UsuarioId, intento.DesdeCache).Should().Be((DisparadorIntento.Reproceso, (Guid?)TableroAyudas.Usuario(token), false));
        var completadasDespues = (await CeldasAsync(empresa, lote.Id, ct)).Where(c => c.Id != celda.Id).OrderBy(c => c.Fuente).ToList();
        completadasDespues.Select(c => (c.Id, c.Estado, c.Payload, c.VersionTablero, c.ActualizadoEn))
            .Should().Equal(completadasAntes.Select(c => (c.Id, c.Estado, c.Payload, c.VersionTablero, c.ActualizadoEn)), "las celdas COMPLETADO no se tocan");
    }

    // AC2 (F03-C14, F04-C9) — el usuario del producto (sin diagnostico.consultas.reprocesar) recibe 403 y la celda sigue en ERROR.
    [Fact]
    public async Task Usuario_del_producto_recibe_403()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 3, vehiculos: [new("TST202", [new(Fuente.Runt, EstadoCelda.Error, 3)])]);
        var celda = lote.Celda("TST202", Fuente.Runt);
        var usuario = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear, Permisos.ConfiguracionVer], roles: ["usuario_diagnostico"]);

        var respuesta = await api.Cliente(usuario).PostAsync(Ruta(celda.Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var despues = await CeldaAsync(empresa, celda.Id, ct);
        (despues.Estado, despues.TipoError, despues.VersionTablero).Should().Be((EstadoCelda.Error, "TIMEOUT", 3L));
        (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).VersionTablero.Should().Be(3);
    }

    // AC3 (F03-C14) — una celda COMPLETADO no se reprocesa: 409 CELDA_NO_EN_ERROR, no cambia y no se llama a la fuente.
    [Fact]
    public async Task Celda_COMPLETADO_responde_409_y_no_cambia()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b => new FuenteRuntEspia(orden).Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 4, estado: EstadoDiagnostico.Completado, vehiculos: [new("TST203", [new(Fuente.Runt, EstadoCelda.Completado, 4)])]);
        var celda = lote.Celda("TST203", Fuente.Runt);
        var antes = await CeldaAsync(empresa, celda.Id, ct);

        var respuesta = await api.Cliente(Admin(api, empresa)).PostAsync(Ruta(celda.Id), null, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be("CELDA_NO_EN_ERROR");
        orden.Should().BeEmpty("no se llama a la fuente");
        var despues = await CeldaAsync(empresa, celda.Id, ct);
        (despues.Estado, despues.Payload, despues.VersionTablero, despues.ActualizadoEn).Should().Be((antes.Estado, antes.Payload, antes.VersionTablero, antes.ActualizadoEn));
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct);
        (cabecera.Estado, cabecera.VersionTablero).Should().Be((EstadoDiagnostico.Completado, 4L));
        (await IntentosAsync(empresa, celda.Id, ct)).Should().BeEmpty();
        (await HistorialAsync(empresa, lote.Id, ct)).Should().BeEmpty("un reproceso rechazado no mueve el lote");
    }

    // R-1 — el endpoint no acepta datos de resultado: lo que venga en el cuerpo se ignora y el dato sale de la consulta real.
    [Fact]
    public async Task Reproceso_no_escribe_resultado_del_cuerpo()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b => new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST204", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 1)])]);
        var celda = lote.Celda("TST204", Fuente.SimitVehiculo);
        using var cuerpo = new StringContent(
            """{"estado":"COMPLETADO","origen":"MANUAL","payload":{"inventado":"DATO-DEL-CUERPO"},"dato_obtenido_en":"2020-01-01T00:00:00Z","usuario_manual_id":"00000000-0000-0000-0000-000000000001"}""",
            Encoding.UTF8, "application/json");

        var respuesta = await api.Cliente(Admin(api, empresa)).PostAsync(Ruta(celda.Id), cuerpo, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        var enCola = await CeldaAsync(empresa, celda.Id, ct);
        (enCola.Estado, enCola.Payload, enCola.Origen, enCola.UsuarioManualId, enCola.DatoObtenidoEn)
            .Should().Be((EstadoCelda.Pendiente, (string?)null, (OrigenCelda?)null, (Guid?)null, (DateTimeOffset?)null));

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var final = await CeldaAsync(empresa, celda.Id, ct);
        final.Estado.Should().Be(EstadoCelda.Completado);
        final.Payload.Should().NotContain("DATO-DEL-CUERPO");
        final.Origen.Should().Be(OrigenCelda.Api);
        final.UsuarioManualId.Should().BeNull();
        final.DatoObtenidoEn.Should().BeAfter(new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero));
        orden.Should().ContainSingle("el dato salió de la consulta real");
    }

    // AC4 (F03-C7, F03-C14) — RUNT en ERROR y SIMIT_VEHICULO / RUNT_PROPIETARIO bloqueadas: reprocesar SIMIT responde 409
    // CELDA_BLOQUEADA_POR_RUNT y no se llama a ninguna fuente.
    [Fact]
    public async Task Celda_bloqueada_por_RUNT_en_error_responde_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            new FuenteRuntEspia(orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.RuntPropietario, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteBloqueadoAsync(api, empresa, "TST205", EstadoDiagnostico.Incompleto, ct);
        var simit = lote.Celda("TST205", Fuente.SimitVehiculo);

        var respuesta = await api.Cliente(Admin(api, empresa)).PostAsync(Ruta(simit.Id), null, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be("CELDA_BLOQUEADA_POR_RUNT");
        orden.Should().BeEmpty("no se llama a ninguna fuente");
        (await MotorRunt.CeldasDelVehiculoAsync(api, simit.VehiculoId, ct)).Should().BeEquivalentTo(
            ["RUNT ERROR TIMEOUT", "SIMIT_VEHICULO ERROR BLOQUEADA_POR_RUNT", "RUNT_PROPIETARIO ERROR BLOQUEADA_POR_RUNT"]);
        (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).Estado.Should().Be(EstadoDiagnostico.Incompleto);
    }

    // AC5 (F03-C7, F03-C14, F04-C6) — lote INCOMPLETO, RUNT en ERROR y dos bloqueadas. Con un fallo inyectado tras marcar RUNT
    // no queda nada a medias; sin él, en la misma transacción RUNT queda COMPLETADO, las bloqueadas vuelven a PENDIENTE y el lote
    // sigue EN_PROCESO; el procesador las ejecuta con REPROCESO y el usuario del reproceso de RUNT, y el lote termina COMPLETADO.
    [Fact]
    public async Task Runt_reprocesado_en_OK_libera_celdas_bloqueadas_en_la_misma_transaccion()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            new FuenteRuntEspia(orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.RuntPropietario, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteBloqueadoAsync(api, empresa, "TST001", EstadoDiagnostico.Incompleto, ct);
        var runt = lote.Celda("TST001", Fuente.Runt);
        var token = Admin(api, empresa);
        var procesador = Motor.Procesador(api);

        (await api.Cliente(token).PostAsync(Ruta(runt.Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var versionTrasReproceso = (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).VersionTablero;

        // Fallo inyectado DESPUÉS de marcar RUNT en COMPLETADO, dentro de la transacción del cierre.
        await Motor.EjecutarAsync(api, """
            CREATE FUNCTION diagnostico.prueba_falla_tras_runt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'falla de prueba tras marcar RUNT'; END $$;
            CREATE TRIGGER tg_prueba_falla_tras_runt AFTER UPDATE ON diagnostico.consultas
            FOR EACH ROW WHEN (NEW.fuente = 'RUNT' AND NEW.estado = 'COMPLETADO') EXECUTE FUNCTION diagnostico.prueba_falla_tras_runt();
            """, ct);
        var fallido = () => procesador.ProcesarLoteAsync(ct);
        await fallido.Should().ThrowAsync<Exception>();

        (await MotorRunt.CeldasDelVehiculoAsync(api, runt.VehiculoId, ct)).Should().BeEquivalentTo(
            ["RUNT EN_CURSO -", "SIMIT_VEHICULO ERROR BLOQUEADA_POR_RUNT", "RUNT_PROPIETARIO ERROR BLOQUEADA_POR_RUNT"], "nada queda a medias");
        (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).VersionTablero.Should().Be(versionTrasReproceso);
        (await IntentosAsync(empresa, runt.Id, ct)).Should().BeEmpty();
        (await MotorRunt.VehiculoAsync(api, empresa, lote.Id, "TST001", ct)).RuntConsultadoEn.Should().BeNull();

        // Sin el fallo: vence el lease, otra vuelta retoma la RUNT y su cierre libera las bloqueadas en la misma transacción.
        await Motor.EjecutarAsync(api, "DROP TRIGGER tg_prueba_falla_tras_runt ON diagnostico.consultas;", ct);
        reloj.Avanzar(TimeSpan.FromMinutes(3));
        (await procesador.ProcesarLoteAsync(ct)).Should().Be(1, "solo la RUNT era tomable");

        (await MotorRunt.CeldasDelVehiculoAsync(api, runt.VehiculoId, ct)).Should().BeEquivalentTo(
            ["RUNT COMPLETADO -", "SIMIT_VEHICULO PENDIENTE -", "RUNT_PROPIETARIO PENDIENTE -"]);
        var celdas = await CeldasAsync(empresa, lote.Id, ct);
        celdas.Select(c => c.VersionTablero).Distinct().Should().ContainSingle("RUNT y sus bloqueadas cambian en la misma transacción");
        celdas.Where(c => c.Fuente != Fuente.Runt).Should().OnlyContain(c => c.IntentosAuto == 0 && c.DisponibleDesde <= reloj.GetUtcNow());
        (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).Estado.Should().Be(EstadoDiagnostico.EnProceso);

        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        orden.Should().Contain([$"SIMIT_VEHICULO {runt.VehiculoId}", $"RUNT_PROPIETARIO {runt.VehiculoId}"], "el procesador las ejecuta sin otra acción del admin");
        (await CeldasAsync(empresa, lote.Id, ct)).Should().OnlyContain(c => c.Estado == EstadoCelda.Completado);
        foreach (var celda in celdas)
        {
            var intento = (await IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle(celda.Fuente.ToString()).Subject;
            (intento.Disparador, intento.UsuarioId).Should().Be((DisparadorIntento.Reproceso, (Guid?)TableroAyudas.Usuario(token)), celda.Fuente.ToString());
        }

        (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).Estado.Should().Be(EstadoDiagnostico.Completado);
    }

    // AC5 (F03-C7) — si el reproceso de la RUNT vuelve a fallar, las bloqueadas siguen en ERROR y no se llama a otra fuente.
    [Fact]
    public async Task Runt_reprocesado_que_vuelve_a_fallar_deja_bloqueadas_en_ERROR()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            new FuenteRuntEspia(orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.RuntPropietario, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteBloqueadoAsync(api, empresa, "TST208", EstadoDiagnostico.Incompleto, ct);
        var runt = lote.Celda("TST208", Fuente.Runt);
        var bloqueadasAntes = (await CeldasAsync(empresa, lote.Id, ct)).Where(c => c.Fuente != Fuente.Runt).OrderBy(c => c.Fuente).ToList();
        var token = Admin(api, empresa);

        (await api.Cliente(token).PostAsync(Ruta(runt.Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        orden.Should().Equal(["RUNT PLACA_CARRO TST208"], "solo se llamó a RUNT");
        (await MotorRunt.CeldasDelVehiculoAsync(api, runt.VehiculoId, ct)).Should().BeEquivalentTo(
            ["RUNT ERROR RESPUESTA_INVALIDA", "SIMIT_VEHICULO ERROR BLOQUEADA_POR_RUNT", "RUNT_PROPIETARIO ERROR BLOQUEADA_POR_RUNT"]);
        (await CeldasAsync(empresa, lote.Id, ct)).Where(c => c.Fuente != Fuente.Runt).OrderBy(c => c.Fuente).Select(c => c.VersionTablero)
            .Should().Equal(bloqueadasAntes.Select(c => c.VersionTablero), "las bloqueadas no se tocan");
        var intento = (await IntentosAsync(empresa, runt.Id, ct)).Should().ContainSingle().Subject;
        (intento.Disparador, intento.UsuarioId, intento.Estado).Should().Be((DisparadorIntento.Reproceso, (Guid?)TableroAyudas.Usuario(token), ResultadoIntento.Error));
    }

    // AC6 (F04-C6, F04-C7, R-8) — lote INCOMPLETO con una celda en ERROR: pasa a EN_PROCESO con historial REPROCESO y el usuario
    // (sin cerrado_en), y al terminar el recalculador lo deja COMPLETADO con historial AUTO.
    [Fact]
    public async Task Lote_INCOMPLETO_vuelve_a_EN_PROCESO_con_historial_REPROCESO_y_se_recalcula()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Incompleto, vehiculos: [new("TST011", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        var token = Admin(api, empresa);

        var respuesta = await api.Cliente(token).PostAsync(Ruta(lote.Celda("TST011", Fuente.Runt).Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("estado_lote").GetString().Should().Be("EN_PROCESO");
        var abierta = await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct);
        (abierta.Estado, abierta.CerradoEn).Should().Be((EstadoDiagnostico.EnProceso, (DateTimeOffset?)null));
        var reabierto = (await HistorialAsync(empresa, lote.Id, ct)).Should().ContainSingle().Subject;
        (reabierto.EstadoAnterior, reabierto.EstadoNuevo, reabierto.Disparador, reabierto.UsuarioId)
            .Should().Be(((EstadoDiagnostico?)EstadoDiagnostico.Incompleto, EstadoDiagnostico.EnProceso, DisparadorEstado.Reproceso, (Guid?)TableroAyudas.Usuario(token)));

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var final = await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct);
        final.Estado.Should().Be(EstadoDiagnostico.Completado);
        final.CerradoEn.Should().NotBeNull();
        var ultima = (await HistorialAsync(empresa, lote.Id, ct)).Last();
        (ultima.EstadoAnterior, ultima.EstadoNuevo, ultima.Disparador, ultima.UsuarioId)
            .Should().Be(((EstadoDiagnostico?)EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, DisparadorEstado.Auto, (Guid?)null));
    }

    // AC2 / fuga (F04-C12) — el admin de A pide reprocesar una celda de B: 404 sin datos de B, y la celda de B no cambia.
    [Fact]
    public async Task Admin_de_otra_compania_recibe_404()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var deB = await TableroAyudas.SembrarAsync(empresas.B, ct, version: 7, estado: EstadoDiagnostico.Incompleto, nombre: "Lote secreto de B", vehiculos:
            [new("TST211", [new(Fuente.Runt, EstadoCelda.Error, 7)])]);
        var celdaDeB = deB.Celda("TST211", Fuente.Runt);

        var respuesta = await api.Cliente(Admin(api, empresas.A)).PostAsync(Ruta(celdaDeB.Id), null, ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, cuerpo);
        cuerpo.Should().NotContainAny(["Lote secreto de B", "TST211", deB.Id.ToString(), deB.Vehiculos.Single().Id.ToString(), empresas.B.Id.ToString()]);
        var despues = await CeldaAsync(empresas.B, celdaDeB.Id, ct);
        (despues.Estado, despues.VersionTablero).Should().Be((EstadoCelda.Error, 7L));
        (await Cabeceras.LeerAsync(api, empresas.B.Id, deB.Id, ct)).Estado.Should().Be(EstadoDiagnostico.Incompleto);
        (await HistorialAsync(empresas.B, deB.Id, ct)).Should().BeEmpty();
    }

    private static string Ruta(Guid consulta) => $"{Program.Prefijo}/consultas/{consulta}/reprocesar";

    /// <summary>Admin de la compañía: <c>admin_diagnostico</c> con los permisos del manifiesto (§6.2).</summary>
    private static string Admin(ApiDePrueba api, EmpresaDePrueba empresa) => api.Token(
        empresa: empresa.Id, roles: ["admin_diagnostico"],
        permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear, Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar]);

    /// <summary>Lote con un vehículo: RUNT en ERROR (TIMEOUT) y SIMIT_VEHICULO y RUNT_PROPIETARIO en ERROR bloqueadas por ella.</summary>
    private static async Task<LoteSembrado> LoteBloqueadoAsync(ApiDePrueba api, EmpresaDePrueba empresa, string placa, EstadoDiagnostico estado, CancellationToken ct)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 2, estado: estado, vehiculos:
            [new(placa, [new(Fuente.Runt, EstadoCelda.Error, 2), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2), new(Fuente.RuntPropietario, EstadoCelda.Error, 2)])]);
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.consultas SET tipo_error = 'BLOQUEADA_POR_RUNT' WHERE diagnostico_id = '{lote.Id}' AND fuente <> 'RUNT';", ct);
        return lote;
    }

    private static async Task<CeldaConsulta> CeldaAsync(EmpresaDePrueba empresa, Guid id, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<CeldaConsulta>().AsNoTracking().SingleAsync(c => c.Id == id, ct);
    }

    private static async Task<List<CeldaConsulta>> CeldasAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<CeldaConsulta>().AsNoTracking().Where(c => c.DiagnosticoId == lote).ToListAsync(ct);
    }

    private static async Task<List<IntentoConsulta>> IntentosAsync(EmpresaDePrueba empresa, Guid celda, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<IntentoConsulta>().AsNoTracking().Where(i => i.ConsultaId == celda).OrderBy(i => i.Inicio).ToListAsync(ct);
    }

    private static async Task<List<HistorialEstadoDiagnostico>> HistorialAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<HistorialEstadoDiagnostico>().AsNoTracking().Where(h => h.DiagnosticoId == lote).OrderBy(h => h.En).ThenBy(h => h.Id).ToListAsync(ct);
    }
}
