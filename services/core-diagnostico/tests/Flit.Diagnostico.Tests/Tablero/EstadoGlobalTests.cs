using System.Net;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13743 (F04-H1) — estado global del lote con el procesador real (fuente RUNT simulada): el recalculador corre tras cada
/// celda terminal en la transacción de cierre y pasa el lote a COMPLETADO con historial AUTO; con una celda en ERROR el lote
/// sigue EN_PROCESO hasta <c>POST /diagnosticos/{id}/cerrar-panel</c>, que lo deja INCOMPLETO con historial CIERRE_PANEL y el
/// usuario. <c>GET /diagnosticos/{id}/historial</c> devuelve las transiciones. Ningún endpoint edita el estado a mano.
/// Placas del simulador: TST001 completa, TST008 responde inválido (error definitivo, sin reintento).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Tablero.EstadoGlobalTests
/// </code>
/// </example>
public sealed class EstadoGlobalTests
{
    // AC3 (F04-C4) — termina la última celda aplicable: el lote pasa a COMPLETADO con historial AUTO, sin usuario.
    [Fact]
    public async Task Ultima_celda_terminada_pasa_el_lote_a_COMPLETADO_con_historial_AUTO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST011")], ct);
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).Estado.Should().Be(EstadoDiagnostico.EnProceso);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Completado);
        cabecera.CerradoEn.Should().NotBeNull();
        var ultima = (await HistorialAsync(empresa, id, ct)).Last();
        (ultima.EstadoAnterior, ultima.EstadoNuevo, ultima.Disparador, ultima.UsuarioId)
            .Should().Be((EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, DisparadorEstado.Auto, (Guid?)null));
        (await HistorialAsync(empresa, id, ct)).Count(h => h.EstadoNuevo == EstadoDiagnostico.Completado).Should().Be(1, "se recalcula tras cada celda pero transiciona una sola vez");
    }

    // AC4 — con una celda en ERROR y todo lo demás terminado, el lote no se cierra solo: sigue EN_PROCESO, sin cierre.
    [Fact]
    public async Task Lote_no_se_cierra_solo_al_completar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST008")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Select(c => c.Estado).Should().BeEquivalentTo([EstadoCelda.Completado, EstadoCelda.Error]);
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.EnProceso);
        cabecera.CerradoEn.Should().BeNull();
        (await HistorialAsync(empresa, id, ct)).Should().NotContain(h => h.EstadoNuevo == EstadoDiagnostico.Completado || h.EstadoNuevo == EstadoDiagnostico.Incompleto);
    }

    // AC4 (F04-C5, F04-C7) — el usuario cierra el panel: INCOMPLETO con historial CIERRE_PANEL y su usuario.
    [Fact]
    public async Task Cerrar_panel_con_error_deja_INCOMPLETO_con_historial_CIERRE_PANEL()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST008")], ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        var token = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);

        var respuesta = await api.Cliente(token).PostAsync(Tablero.RutaCierrePanel(id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await Tablero.JsonAsync(respuesta, ct);
        cuerpo.GetProperty("id").GetGuid().Should().Be(id);
        cuerpo.GetProperty("estado").GetString().Should().Be("INCOMPLETO");
        cuerpo.TryGetProperty("cerrado_en", out var cerradoEn).Should().BeTrue();
        cerradoEn.GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Incompleto);
        cabecera.CerradoEn.Should().NotBeNull();
        var ultima = (await HistorialAsync(empresa, id, ct)).Last();
        (ultima.EstadoAnterior, ultima.EstadoNuevo, ultima.Disparador, ultima.UsuarioId)
            .Should().Be((EstadoDiagnostico.EnProceso, EstadoDiagnostico.Incompleto, DisparadorEstado.CierrePanel, (Guid?)Tablero.Usuario(token)));

        var otraVez = await api.Cliente(token).PostAsync(Tablero.RutaCierrePanel(id), null, ct);
        otraVez.StatusCode.Should().Be(HttpStatusCode.Conflict, "un lote ya cerrado no se vuelve a cerrar");
        (await Tablero.JsonAsync(otraVez, ct)).GetProperty("code").GetString().Should().Be("LOTE_NO_EN_PROCESO");
    }

    // AC4 (F04-C7) — el historial del lote se consulta por API: anterior, nuevo, disparador, usuario y fecha, en orden.
    [Fact]
    public async Task Historial_consultable_por_API()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST008")], ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        var token = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        (await api.Cliente(token).PostAsync(Tablero.RutaCierrePanel(id), null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var respuesta = await api.Cliente(token).GetAsync(Tablero.RutaHistorial(id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var historial = await Tablero.JsonAsync(respuesta, ct);
        historial.GetProperty("diagnostico_id").GetGuid().Should().Be(id);
        historial.GetProperty("estado").GetString().Should().Be("INCOMPLETO");
        var transiciones = historial.GetProperty("transiciones").EnumerateArray().ToList();
        transiciones.Select(t => $"{(t.GetProperty("estado_anterior").ValueKind == System.Text.Json.JsonValueKind.Null ? "-" : t.GetProperty("estado_anterior").GetString())}>{t.GetProperty("estado_nuevo").GetString()} {t.GetProperty("disparador").GetString()}")
            .Should().Equal(["->CREADO AUTO", "CREADO>EN_PROCESO AUTO", "EN_PROCESO>INCOMPLETO CIERRE_PANEL"]);
        transiciones[^1].GetProperty("usuario_id").GetGuid().Should().Be(Tablero.Usuario(token));
        transiciones[1].GetProperty("usuario_id").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "lo disparó el procesador");
        transiciones.Select(t => t.GetProperty("en").GetDateTimeOffset()).Should().BeInAscendingOrder();
    }

    // AC4 — el cierre del panel solo procede con ERROR y todo terminado: si no, 409 con código claro y nada cambia.
    [Fact]
    public async Task Cerrar_panel_sin_error_o_con_celdas_pendientes_responde_409()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var pendiente = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST161", [new(Fuente.Runt, EstadoCelda.Error, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)])]);
        var sinError = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST162", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Na, 1)])]);
        var completo = await Tablero.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Completado, vehiculos: [new("TST163", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        foreach (var (lote, codigo, estado) in new[]
                 {
                     (pendiente, "CELDAS_SIN_TERMINAR", EstadoDiagnostico.EnProceso), (sinError, "LOTE_SIN_ERRORES", EstadoDiagnostico.EnProceso),
                     (completo, "LOTE_NO_EN_PROCESO", EstadoDiagnostico.Completado),
                 })
        {
            var respuesta = await empresa.Cliente().PostAsync(Tablero.RutaCierrePanel(lote.Id), null, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, codigo);
            (await Tablero.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be(codigo);
            (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).Estado.Should().Be(estado);
            (await HistorialAsync(empresa, lote.Id, ct)).Should().BeEmpty("un cierre rechazado no escribe historial");
        }
    }

    // Ningún endpoint edita el estado: no hay PUT/PATCH del estado del lote.
    [Fact]
    public async Task Ningun_endpoint_edita_el_estado_del_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        using var cliente = api.Cliente(api.Token(permisos: [.. Permisos.Todos]));
        var id = Guid.NewGuid();

        foreach (var metodo in new[] { HttpMethod.Put, HttpMethod.Patch })
        {
            var respuesta = await cliente.SendAsync(new HttpRequestMessage(metodo, $"{Cabeceras.Ruta}/{id}/estado") { Content = new StringContent("""{"estado":"COMPLETADO"}""", System.Text.Encoding.UTF8, "application/json") }, ct);
            respuesta.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }
    }

    private static async Task<List<HistorialEstadoDiagnostico>> HistorialAsync(EmpresaDePrueba empresa, Guid id, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<HistorialEstadoDiagnostico>().AsNoTracking().Where(h => h.DiagnosticoId == id).OrderBy(h => h.En).ThenBy(h => h.Id).ToListAsync(ct);
    }
}
