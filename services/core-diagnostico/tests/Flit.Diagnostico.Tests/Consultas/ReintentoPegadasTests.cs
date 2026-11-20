using System.Net;
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
/// Ajuste 2026-11-11 Punto 3 (decisiones 3.2–3.4, AC 3–7 y 9) — reintento manual de las celdas «pegadas» (ERROR, o PENDIENTE/EN_CURSO
/// con más de 10 min sin cambio): <c>POST /diagnosticos/{id}/vehiculos/{vid}/reintentar</c> (las de esa placa) y
/// <c>POST /diagnosticos/{id}/reintentar-pegadas</c> (las del lote), con <c>diagnostico.consultas.reprocesar</c>. Responden
/// <c>{ "reencoladas": n }</c>. Cada celda reencolada sale por el camino del reproceso: PENDIENTE, <c>intentos_auto=0</c>,
/// <c>disponible_desde=ahora</c> y trabajo publicado con <c>trabajo_id</c> NUEVO (la respuesta tardía del anterior se descarta). Las
/// completas y las recientes no se tocan; pulsar dos veces no reencola de nuevo. PostgreSQL real (las de 403, sin base).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReintentoPegadasTests
/// </code>
/// </example>
public sealed class ReintentoPegadasTests
{
    // AC3, AC7 — por placa: solo sus celdas pegadas (ERROR y EN_CURSO de 11 min); la COMPLETADO y la EN_CURSO de 9 min, intactas.
    // Cada reencolada lleva trabajo_id nuevo e intentos_auto=0.
    [Fact]
    public async Task Reintentar_placa_reencola_solo_sus_celdas_pegadas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 4, vehiculos:
        [
            new("TST301", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2),
                new(Fuente.Avaluo, EstadoCelda.EnCurso, 3), new(Fuente.ImpuestoDepartamental, EstadoCelda.EnCurso, 3)]),
            new("TST302", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)]),
        ]);
        var simit = lote.Celda("TST301", Fuente.SimitVehiculo);
        var avaluo = lote.Celda("TST301", Fuente.Avaluo);
        var impuesto = lote.Celda("TST301", Fuente.ImpuestoDepartamental);
        var viejo = Guid.CreateVersion7();
        await HaceMinutosAsync(api, avaluo.Id, 11, ct, trabajo: viejo);
        await HaceMinutosAsync(api, impuesto.Id, 9, ct);
        await HaceMinutosAsync(api, simit.Id, 1, ct, trabajo: Guid.CreateVersion7());
        var runt = await CeldaAsync(empresa, lote.Celda("TST301", Fuente.Runt).Id, ct);
        var impuestoAntes = await CeldaAsync(empresa, impuesto.Id, ct);
        var otraPlaca = await CeldaAsync(empresa, lote.Celda("TST302", Fuente.SimitVehiculo).Id, ct);

        var respuesta = await api.Cliente(Admin(api, empresa)).PostAsync(RutaPlaca(lote.Id, simit.VehiculoId), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("reencoladas").GetInt32().Should().Be(2);
        foreach (var (id, anterior) in new[] { (simit.Id, (Guid?)null), (avaluo.Id, (Guid?)viejo) })
        {
            var celda = await CeldaAsync(empresa, id, ct);
            celda.Estado.Should().BeOneOf([EstadoCelda.Pendiente, EstadoCelda.EnCurso], "reencolada (EN_CURSO en cuanto su trabajo sale)");
            (celda.IntentosAuto, celda.TipoError).Should().Be((0, (string?)null), "reinicia sus 3 automáticos (decisión 3.3)");
            celda.TrabajoId.Should().NotBeNull();
            if (anterior is { } a)
                celda.TrabajoId.Should().NotBe(a, "trabajo_id nuevo: la respuesta tardía del anterior se descarta");
            celda.DisponibleDesde.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
            celda.ReprocesoUsuarioId.Should().NotBeNull("sale por el camino del reproceso, con el usuario");
        }

        (await CeldaAsync(empresa, runt.Id, ct)).Should().BeEquivalentTo(runt, o => o.Excluding(c => c.Version), "la COMPLETADO no se toca");
        (await CeldaAsync(empresa, impuesto.Id, ct)).Should().BeEquivalentTo(impuestoAntes, o => o.Excluding(c => c.Version), "9 min no es pegada");
        (await CeldaAsync(empresa, otraPlaca.Id, ct)).Estado.Should().Be(EstadoCelda.Error, "otra placa no se toca");
        (await OutboxDePrueba.TrabajosAsync(api, ct)).Select(t => t.ConsultaId).Should().BeEquivalentTo([simit.Id, avaluo.Id]);
    }

    // AC5 — idempotente: la segunda pulsación encuentra las celdas recién reencoladas (menos de 10 min) y no publica nada más.
    [Fact]
    public async Task Pulsar_dos_veces_no_duplica_consultas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 2, vehiculos:
            [new("TST303", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)])]);
        var celda = lote.Celda("TST303", Fuente.SimitVehiculo);
        using var cliente = api.Cliente(Admin(api, empresa));

        var primera = await cliente.PostAsync(RutaPlaca(lote.Id, celda.VehiculoId), null, ct);
        var trabajo = (await CeldaAsync(empresa, celda.Id, ct)).TrabajoId;
        var segunda = await cliente.PostAsync(RutaPlaca(lote.Id, celda.VehiculoId), null, ct);
        var deLote = await cliente.PostAsync(RutaLote(lote.Id), null, ct);

        (await TableroAyudas.JsonAsync(primera, ct)).GetProperty("reencoladas").GetInt32().Should().Be(1);
        (await TableroAyudas.JsonAsync(segunda, ct)).GetProperty("reencoladas").GetInt32().Should().Be(0);
        (await TableroAyudas.JsonAsync(deLote, ct)).GetProperty("reencoladas").GetInt32().Should().Be(0);
        (await CeldaAsync(empresa, celda.Id, ct)).TrabajoId.Should().Be(trabajo, "la segunda pulsación no cambia el trabajo");
        (await OutboxDePrueba.TrabajosAsync(api, ct)).Should().ContainSingle("una sola consulta publicada");
    }

    // AC4 — masivo: todas las celdas pegadas del lote (de varias placas); las completas no; un lote cerrado vuelve a EN_PROCESO.
    [Fact]
    public async Task Reintentar_pegadas_reencola_todas_las_del_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 6, estado: EstadoDiagnostico.Incompleto, vehiculos:
        [
            new("TST304", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)]),
            new("TST305", [new(Fuente.Runt, EstadoCelda.Error, 3), new(Fuente.SimitVehiculo, EstadoCelda.Error, 3)]),
            new("TST306", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 1)]),
        ]);
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.consultas SET tipo_error = 'BLOQUEADA_POR_RUNT' WHERE id = '{lote.Celda("TST305", Fuente.SimitVehiculo).Id}'", ct);

        var respuesta = await api.Cliente(Admin(api, empresa)).PostAsync(RutaLote(lote.Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("reencoladas").GetInt32().Should().Be(2, "SIMIT de TST304 y la RUNT de TST305 (su SIMIT espera a la RUNT)");
        (await OutboxDePrueba.TrabajosAsync(api, ct)).Select(t => t.ConsultaId)
            .Should().BeEquivalentTo([lote.Celda("TST304", Fuente.SimitVehiculo).Id, lote.Celda("TST305", Fuente.Runt).Id]);
        (await CeldaAsync(empresa, lote.Celda("TST305", Fuente.SimitVehiculo).Id, ct)).TipoError.Should().Be(OrquestadorVehiculo.BloqueadaPorRunt, "la libera el cierre de su RUNT");
        (await CeldaAsync(empresa, lote.Celda("TST306", Fuente.SimitVehiculo).Id, ct)).Estado.Should().Be(EstadoCelda.Completado);
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct);
        (cabecera.Estado, cabecera.CerradoEn).Should().Be((EstadoDiagnostico.EnProceso, (DateTimeOffset?)null), "como el reproceso, reabre el lote");
    }

    // AC6 — la respuesta tardía del trabajo anterior se descarta y la celda queda con el resultado del trabajo nuevo.
    [Fact]
    public async Task La_respuesta_tardia_del_trabajo_anterior_se_descarta()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        var anterior = (await bus.DescartarTrabajosAsync(ct)).Single();
        var celda = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        await HaceMinutosAsync(api, celda.Id, 11, ct);

        var respuesta = await api.Cliente(Admin(api, empresa)).PostAsync(RutaPlaca(lote, celda.VehiculoId), null, ct);
        (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("reencoladas").GetInt32().Should().Be(1);
        var nueva = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        nueva.TrabajoId.Should().NotBe(anterior.TrabajoId);

        var tardia = bus.Resultado(anterior, await ConsultasSimuladas.ResponderAsync(api, anterior, ct));
        (await bus.EntregarAsync(tardia, ct)).Should().BeTrue("se confirma sin efecto");
        var trasTardia = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        (trasTardia.Estado, trasTardia.TrabajoId, trasTardia.VersionTablero).Should().Be((EstadoCelda.EnCurso, nueva.TrabajoId, nueva.VersionTablero), "no pisa la celda");

        await MotorRunt.ProcesarTodoAsync(bus, ct);
        var final = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        (final.Estado, final.TrabajoId).Should().Be((EstadoCelda.Completado, nueva.TrabajoId), "el resultado es el del trabajo nuevo");
        (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle(i => i.Disparador == DisparadorIntento.Reproceso);
    }

    // Placa de otro lote u otra compañía: 404 sin cambios.
    [Fact]
    public async Task Vehiculo_de_otro_lote_o_lote_de_otra_compania_responde_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var deA = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos: [new("TST307", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        var otroDeA = await TableroAyudas.SembrarAsync(empresas.A, ct, nombre: "Otro", vehiculos: [new("TST308", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        var deB = await TableroAyudas.SembrarAsync(empresas.B, ct, vehiculos: [new("TST309", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        using var cliente = api.Cliente(Admin(api, empresas.A));

        (await cliente.PostAsync(RutaPlaca(deA.Id, otroDeA.Vehiculos.Single().Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.PostAsync(RutaPlaca(deB.Id, deB.Vehiculos.Single().Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.PostAsync(RutaLote(deB.Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OutboxDePrueba.TrabajosAsync(api, ct)).Should().BeEmpty();
        (await CeldaAsync(empresas.B, deB.Celdas.Single().Id, ct)).Estado.Should().Be(EstadoCelda.Error);
    }

    // AC9 — sin diagnostico.consultas.reprocesar, ambas acciones responden 403.
    [Fact]
    public async Task Sin_permiso_de_reprocesar_ambas_responden_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var empresa = Guid.NewGuid();
        using var cliente = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear], roles: ["usuario_diagnostico"]));

        foreach (var ruta in new[] { RutaPlaca(Guid.NewGuid(), Guid.NewGuid()), RutaLote(Guid.NewGuid()) })
        {
            var respuesta = await cliente.PostAsync(ruta, null, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, ruta);
            (await respuesta.Content.ReadAsStringAsync(ct)).Should().NotContain("PRODUCTO_NO_HABILITADO", "el 403 es del permiso, no del guard de suscripción");
        }

        // Y la ruta existe con el permiso exigido (no es un 403 genérico): el endpoint declara diagnostico.consultas.reprocesar.
        var rutas = api.App.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(f => f.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IRouteNameMetadata>()?.RouteName is "ReintentarPlaca" or "ReintentarPegadasLote")
            .ToList();
        var prefijo = Program.Prefijo.TrimStart('/');
        rutas.Select(e => (e.RoutePattern.RawText ?? string.Empty).TrimStart('/')).Should().BeEquivalentTo(
            [$"{prefijo}/diagnosticos/{{id:guid}}/vehiculos/{{vid:guid}}/reintentar", $"{prefijo}/diagnosticos/{{id:guid}}/reintentar-pegadas"]);
    }

    private static string RutaPlaca(Guid lote, Guid vehiculo) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{lote}/vehiculos/{vehiculo}/reintentar";

    private static string RutaLote(Guid lote) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{lote}/reintentar-pegadas";

    private static string Admin(ApiDePrueba api, EmpresaDePrueba empresa) => api.Token(
        empresa: empresa.Id, roles: ["admin_diagnostico"],
        permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear, Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar]);

    /// <summary>Deja la celda con su último cambio hace <paramref name="minutos"/> min (y, si se da, ese trabajo vigente).</summary>
    internal static Task HaceMinutosAsync(ApiDePrueba api, Guid celda, int minutos, CancellationToken ct, Guid? trabajo = null) =>
        Motor.EjecutarAsync(api,
            $"UPDATE diagnostico.consultas SET actualizado_en = now() - interval '{minutos} minutes'"
            + (trabajo is { } t ? $", trabajo_id = '{t}'" : string.Empty) + $" WHERE id = '{celda}'", ct);

    private static async Task<CeldaConsulta> CeldaAsync(EmpresaDePrueba empresa, Guid id, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<CeldaConsulta>().AsNoTracking().SingleAsync(c => c.Id == id, ct);
    }
}
