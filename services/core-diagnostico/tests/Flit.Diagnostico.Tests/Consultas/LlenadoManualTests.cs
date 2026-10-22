using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
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
/// HU #13800 (F06-H3, F06-C7..C11) — llenado manual de una celda en ERROR desde el backoffice FLIT:
/// <c>PUT /backoffice/consultas/{cid}/manual?empresa=</c> con <c>diagnostico.consultas.llenar</c> y la política del backoffice.
/// La celda queda COMPLETADO con <c>origen=MANUAL</c> y el usuario, con un intento <c>MANUAL</c> sin transacción (que no consume),
/// el tablero sube de versión y el lote cerrado vuelve a EN_PROCESO (historial MANUAL) y se recalcula en la misma transacción.
/// Solo celdas en ERROR no bloqueadas por RUNT, nunca RUNT ni IMPUESTO_DEPARTAMENTAL (Q3); en las de persona, solo las personas en
/// error. El cuerpo se valida contra el tipo normalizado de la fuente. Postgres real; placas TST### y documentos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~LlenadoManualTests
/// </code>
/// </example>
public sealed class LlenadoManualTests
{
    private const string SimitManual =
        """{"semaforo":"AMARILLO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0001","fecha":"2026-09-30","valor":"468500","estado":"PENDIENTE","organismo":"Organismo de prueba"}]}""";

    private static string Ruta(Guid consulta, Guid empresa) => $"{Program.Prefijo}/backoffice/consultas/{consulta}/manual?empresa={empresa}";

    private static string Cuerpo(long version, string resultado) => $$"""{"version":{{version}},"resultado":{{resultado}}}""";

    // AC1 (F06-C9) — SIMIT_VEHICULO en ERROR de A con ?empresa=A: COMPLETADO, origen MANUAL y su usuario; la compañía no lo distingue.
    [Fact]
    public async Task Backoffice_llena_celda_en_error_y_queda_COMPLETADO_MANUAL()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, version: 5, vehiculos:
        [
            new("TST701", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)]),
            new("TST702", [new(Fuente.Runt, EstadoCelda.Completado, 3), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 4)]),
        ]);
        var celda = lote.Celda("TST701", Fuente.SimitVehiculo);
        var token = TokenLlenado(api);

        var respuesta = await Llenar(api, token, celda.Id, empresas.A.Id, Cuerpo(2, SimitManual), ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var dto = JsonDocument.Parse(cuerpo).RootElement;
        dto.GetProperty("id").GetGuid().Should().Be(celda.Id);
        dto.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        dto.GetProperty("origen").GetString().Should().Be("MANUAL");
        dto.GetProperty("usuario_manual_id").GetGuid().Should().Be(TableroAyudas.Usuario(token));
        dto.GetProperty("version").GetInt64().Should().Be(6, "el llenado sube la versión del tablero");

        var guardada = await CeldaAsync(empresas.A, celda.Id, ct);
        guardada.Estado.Should().Be(EstadoCelda.Completado);
        guardada.Origen.Should().Be(OrigenCelda.Manual);
        guardada.UsuarioManualId.Should().Be(TableroAyudas.Usuario(token));
        guardada.TipoError.Should().BeNull();
        guardada.DatoObtenidoEn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), "la fecha del dato la fija el servidor");
        guardada.VersionTablero.Should().Be(6);
        JsonDocument.Parse(guardada.Payload!).RootElement.GetProperty("cantidad_comparendos").GetInt32().Should().Be(1);

        // R-20: el detalle de la compañía de la celda manual es idéntico al de una celda de API con el mismo dato.
        await TableroAyudas.PayloadAsync(empresas.A, lote.Celda("TST702", Fuente.SimitVehiculo), guardada.Payload, ct);
        var manual = await TableroAyudas.JsonAsync(await empresas.A.Cliente().GetAsync(TableroAyudas.RutaDetalle(lote.Id, celda.VehiculoId, "SIMIT_VEHICULO"), ct), ct);
        var deApi = await TableroAyudas.JsonAsync(await empresas.A.Cliente().GetAsync(TableroAyudas.RutaDetalle(lote.Id, lote.Celda("TST702", Fuente.SimitVehiculo).VehiculoId, "SIMIT_VEHICULO"), ct), ct);
        manual.EnumerateObject().Select(p => p.Name).Should().Equal(deApi.EnumerateObject().Select(p => p.Name));
        manual.GetProperty("resultado").GetRawText().Should().Be(deApi.GetProperty("resultado").GetRawText());
        manual.GetRawText().Should().NotContainAny(["MANUAL", TableroAyudas.Usuario(token).ToString()]);
    }

    // AC1 (F06-C9) — el intento queda MANUAL, con el usuario, sin servicio ni transacción del proveedor y sin consumo.
    [Fact]
    public async Task Intento_MANUAL_con_usuario_y_sin_transaccion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos:
            [new("TST711", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 1)])]);
        var celda = lote.Celda("TST711", Fuente.SimitVehiculo);
        var token = TokenLlenado(api);

        (await Llenar(api, token, celda.Id, empresas.A.Id, Cuerpo(1, SimitManual), ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var intento = (await IntentosAsync(empresas.A, celda.Id, ct)).Should().ContainSingle().Subject;
        intento.Disparador.Should().Be(DisparadorIntento.Manual);
        intento.UsuarioId.Should().Be(TableroAyudas.Usuario(token));
        intento.TransaccionProveedorId.Should().BeNull();
        intento.Servicio.Should().BeNull();
        intento.DesdeCache.Should().BeFalse();
        intento.Estado.Should().Be(ResultadoIntento.Success);
        intento.TipoError.Should().BeNull();
        intento.PersonaId.Should().BeNull();
        intento.TenantId.Should().Be(empresas.A.Id, "el intento es de la compañía de la celda, no de FLIT");
    }

    // AC2 (F06-C10) — lote INCOMPLETO sin más errores: INCOMPLETO → EN_PROCESO (MANUAL) y EN_PROCESO → COMPLETADO; la versión sube.
    [Fact]
    public async Task Lote_INCOMPLETO_vuelve_a_EN_PROCESO_y_se_recalcula()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, version: 7, estado: EstadoDiagnostico.Incompleto, vehiculos:
            [new("TST721", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)])]);
        var otro = await TableroAyudas.SembrarAsync(empresas.A, ct, version: 3, estado: EstadoDiagnostico.Incompleto, vehiculos:
            [new("TST722", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2), new(Fuente.Avaluo, EstadoCelda.Error, 3)])]);
        var token = TokenLlenado(api);

        var respuesta = await Llenar(api, token, lote.Celda("TST721", Fuente.SimitVehiculo).Id, empresas.A.Id, Cuerpo(2, SimitManual), ct);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain("\"estado_lote\":\"COMPLETADO\"");
        (await Llenar(api, token, otro.Celda("TST722", Fuente.SimitVehiculo).Id, empresas.A.Id, Cuerpo(2, SimitManual), ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = empresas.A.Db();
        var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == lote.Id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Completado);
        cabecera.VersionTablero.Should().Be(8);
        cabecera.CerradoEn.Should().NotBeNull();
        var historial = await db.Set<HistorialEstadoDiagnostico>().AsNoTracking()
            .Where(h => h.DiagnosticoId == lote.Id).OrderBy(h => h.En).ThenBy(h => h.Id).ToListAsync(ct);
        historial.Select(h => (h.EstadoAnterior, h.EstadoNuevo, h.Disparador, h.UsuarioId)).Should().Equal(
            (EstadoDiagnostico.Incompleto, EstadoDiagnostico.EnProceso, DisparadorEstado.Manual, (Guid?)TableroAyudas.Usuario(token)),
            (EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, DisparadorEstado.Auto, (Guid?)null));

        // Con otra celda en ERROR el lote reabierto se queda EN_PROCESO hasta el cierre del panel.
        var otraCabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == otro.Id, ct);
        otraCabecera.Estado.Should().Be(EstadoDiagnostico.EnProceso);
        otraCabecera.CerradoEn.Should().BeNull();
        (await db.Set<HistorialEstadoDiagnostico>().AsNoTracking().Where(h => h.DiagnosticoId == otro.Id).ToListAsync(ct))
            .Should().ContainSingle().Which.Disparador.Should().Be(DisparadorEstado.Manual);

        // Q5: la compañía ve la reapertura como AUTO y sin usuario.
        var compania = await TableroAyudas.JsonAsync(await empresas.A.Cliente().GetAsync(TableroAyudas.RutaHistorial(lote.Id), ct), ct);
        compania.GetRawText().Should().NotContain("MANUAL").And.NotContain(TableroAyudas.Usuario(token).ToString());
    }

    // AC3 (F06-C7) — el admin de A, el usuario del producto y el backoffice sin diagnostico.consultas.llenar: 403 y la celda no cambia.
    [Fact]
    public async Task Admin_y_usuario_de_la_compania_reciben_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos:
            [new("TST731", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 1)])]);
        var celda = lote.Celda("TST731", Fuente.SimitVehiculo);
        var admin = api.Token(empresa: empresas.A.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]);
        var sinLlenar = api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer, Permisos.AuditoriaVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

        foreach (var token in new[] { admin, empresas.A.Token, sinLlenar })
            (await Llenar(api, token, celda.Id, empresas.A.Id, Cuerpo(1, SimitManual), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var guardada = await CeldaAsync(empresas.A, celda.Id, ct);
        guardada.Estado.Should().Be(EstadoCelda.Error);
        guardada.Origen.Should().BeNull();
        (await IntentosAsync(empresas.A, celda.Id, ct)).Should().BeEmpty();
    }

    // AC4 (F06-C8) — celda COMPLETADO (también el avalúo «sin valor comercial», Q4) o ERROR BLOQUEADA_POR_RUNT: 409 sin cambios.
    [Fact]
    public async Task Celda_verde_o_bloqueada_por_RUNT_responde_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos:
        [
            new("TST741", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 1), new(Fuente.Avaluo, EstadoCelda.Error, 1)]),
            new("TST742", [new(Fuente.Runt, EstadoCelda.Error, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 1)]),
        ]);
        var sinValor = await TableroAyudas.PayloadAsync(empresas.A, lote.Celda("TST741", Fuente.Avaluo), """{"sin_valor_comercial":true,"fuentes":[]}""", ct);
        var bloqueada = await TableroAyudas.PayloadAsync(empresas.A, lote.Celda("TST742", Fuente.SimitVehiculo), null, ct, EstadoCelda.Error, "BLOQUEADA_POR_RUNT");
        var token = TokenLlenado(api);

        var verde = await Llenar(api, token, lote.Celda("TST741", Fuente.SimitVehiculo).Id, empresas.A.Id, Cuerpo(1, SimitManual), ct);
        verde.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(verde, ct)).GetProperty("code").GetString().Should().Be("CELDA_NO_EN_ERROR");
        var avaluo = await Llenar(api, token, sinValor.Id, empresas.A.Id, Cuerpo(1, """{"sin_valor_comercial":true,"fuentes":[]}"""), ct);
        avaluo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(avaluo, ct)).GetProperty("code").GetString().Should().Be("CELDA_NO_EN_ERROR");
        var porRunt = await Llenar(api, token, bloqueada.Id, empresas.A.Id, Cuerpo(1, SimitManual), ct);
        porRunt.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(porRunt, ct)).GetProperty("code").GetString().Should().Be("CELDA_BLOQUEADA_POR_RUNT");

        (await CeldaAsync(empresas.A, bloqueada.Id, ct)).Should().Match<CeldaConsulta>(c => c.Estado == EstadoCelda.Error && c.Origen == null);
        (await CeldaAsync(empresas.A, sinValor.Id, ct)).Origen.Should().Be(OrigenCelda.Api);
        (await IntentosAsync(empresas.A, bloqueada.Id, ct)).Should().BeEmpty();
    }

    // AC5 (F06-C8, Q3, R-9) — RUNT e IMPUESTO_DEPARTAMENTAL en ERROR: 422 FUENTE_NO_ADMITE_MANUAL.
    [Fact]
    public async Task RUNT_e_impuesto_responden_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos:
            [new("TST751", [new(Fuente.Runt, EstadoCelda.Error, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Error, 1)])]);
        var token = TokenLlenado(api);

        foreach (var fuente in new[] { Fuente.Runt, Fuente.ImpuestoDepartamental })
        {
            var respuesta = await Llenar(api, token, lote.Celda("TST751", fuente).Id, empresas.A.Id, Cuerpo(1, """{"placa":"TST751"}"""), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, fuente.ToString());
            (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be("FUENTE_NO_ADMITE_MANUAL");
            (await CeldaAsync(empresas.A, lote.Celda("TST751", fuente).Id, ct)).Estado.Should().Be(EstadoCelda.Error);
        }
    }

    // AC6 (F06-C8) — un campo que no existe en el esquema de AVALUO (y otros cuerpos fuera del esquema): 422 RESULTADO_INVALIDO con el campo.
    [Fact]
    public async Task Cuerpo_fuera_del_esquema_responde_422_con_campo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos:
            [new("TST761", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.Avaluo, EstadoCelda.Error, 1)])]);
        var celda = lote.Celda("TST761", Fuente.Avaluo);
        var token = TokenLlenado(api);
        const string Fasecolda = """{"fuente":"fasecolda","estado":"OK","valor":48500000,"muestras":3}""";

        (string Cuerpo, string Campo)[] invalidos =
        [
            (Cuerpo(1, $$"""{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{{Fasecolda}}],"proveedor":"intempo"}"""), "resultado.proveedor"),
            (Cuerpo(1, """{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{"fuente":"fasecolda","estado":"OK"}]}"""), "resultado.fuentes[0].valor"),
            (Cuerpo(1, $$"""{"sin_valor_comercial":false,"valor_sugerido":"mucho","fuente_principal":"fasecolda","fuentes":[{{Fasecolda}}]}"""), "resultado.valor_sugerido"),
            (Cuerpo(1, """{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{"fuente":"fasecolda","estado":"OK","valor":48500000,"fecha":"2020-01-01"}]}"""), "resultado.fuentes[0].fecha"),
            ("""{"version":1}""", "resultado"),
            ("""{"resultado":{"sin_valor_comercial":true,"fuentes":[]}}""", "version"),
        ];
        foreach (var (cuerpo, campo) in invalidos)
        {
            var respuesta = await Llenar(api, token, celda.Id, empresas.A.Id, cuerpo, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, campo);
            var error = await TableroAyudas.JsonAsync(respuesta, ct);
            error.GetProperty("code").GetString().Should().Be("RESULTADO_INVALIDO", campo);
            error.GetProperty("campo").GetString().Should().Be(campo);
        }

        (await CeldaAsync(empresas.A, celda.Id, ct)).Estado.Should().Be(EstadoCelda.Error, "ningún cuerpo inválido cambia la celda");

        var valido = await Llenar(api, token, celda.Id, empresas.A.Id,
            Cuerpo(1, $$"""{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{{Fasecolda}}]}"""), ct);
        valido.StatusCode.Should().Be(HttpStatusCode.OK, await valido.Content.ReadAsStringAsync(ct));
        var payload = JsonDocument.Parse((await CeldaAsync(empresas.A, celda.Id, ct)).Payload!).RootElement;
        payload.GetProperty("valor_sugerido").GetInt64().Should().Be(48500000);
        payload.GetProperty("fuentes")[0].GetProperty("fecha").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$", "la fecha la fija el servidor");
    }

    // F03-H8 / CELDA_MODIFICADA — el admin reprocesa la celda mientras el backoffice la llena con la versión vieja: 409 y gana el reproceso.
    [Fact]
    public async Task Llenado_concurrente_con_reproceso_responde_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, version: 4, vehiculos:
            [new("TST771", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 4)])]);
        var celda = lote.Celda("TST771", Fuente.SimitVehiculo);
        var admin = api.Token(empresa: empresas.A.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]);
        (await api.Cliente(admin).PostAsync($"{Program.Prefijo}/consultas/{celda.Id}/reprocesar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = TokenLlenado(api);

        var vieja = await Llenar(api, token, celda.Id, empresas.A.Id, Cuerpo(4, SimitManual), ct);

        vieja.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(vieja, ct)).GetProperty("code").GetString().Should().Be("CELDA_MODIFICADA");
        var enCola = await CeldaAsync(empresas.A, celda.Id, ct);
        enCola.Estado.Should().Be(EstadoCelda.Pendiente);
        enCola.ReprocesoUsuarioId.Should().Be(TableroAyudas.Usuario(admin));
        var nueva = await Llenar(api, token, celda.Id, empresas.A.Id, Cuerpo(enCola.VersionTablero, SimitManual), ct);
        nueva.StatusCode.Should().Be(HttpStatusCode.Conflict, "con la versión al día la celda ya no está en ERROR");
        (await TableroAyudas.JsonAsync(nueva, ct)).GetProperty("code").GetString().Should().Be("CELDA_NO_EN_ERROR");
        (await IntentosAsync(empresas.A, celda.Id, ct)).Should().BeEmpty();
    }

    // AC9 (F06-C7, F06-C9, fuga) — ?empresa=B con una celda de A: 404 sin datos de A y ninguna celda cambia.
    [Fact]
    public async Task Empresa_distinta_a_la_de_la_celda_responde_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos:
            [new("TST781", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 1)])]);
        var deB = await TableroAyudas.SembrarAsync(empresas.B, ct, vehiculos:
            [new("TST781", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 1)])]);
        var celda = lote.Celda("TST781", Fuente.SimitVehiculo);
        var token = TokenLlenado(api);

        var respuesta = await Llenar(api, token, celda.Id, empresas.B.Id, Cuerpo(1, SimitManual), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        cuerpo.Should().NotContainAny([lote.Id.ToString(), celda.VehiculoId.ToString(), empresas.A.Id.ToString()]);
        (await CeldaAsync(empresas.A, celda.Id, ct)).Should().Match<CeldaConsulta>(c => c.Estado == EstadoCelda.Error && c.Origen == null);
        (await CeldaAsync(empresas.B, deB.Celda("TST781", Fuente.SimitVehiculo).Id, ct)).Estado.Should().Be(EstadoCelda.Error);
        (await IntentosAsync(empresas.A, celda.Id, ct)).Should().BeEmpty();
    }

    // AC7 (F06-C8, F06-C9) — SIMIT_PROPIETARIO con un propietario OK por API y otro en ERROR: se llena solo el segundo.
    [Fact]
    public async Task Fuente_de_persona_llena_solo_las_personas_en_error()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var (lote, celda, primero, segundo) = await CeldaDePersonasAsync(api, empresas.A, "TST791", ct);
        var token = TokenLlenado(api);
        var antes = JsonDocument.Parse((await CeldaAsync(empresas.A, celda.Id, ct)).Payload!).RootElement.GetProperty("personas")[0].GetRawText();

        var ok = await Llenar(api, token, celda.Id, empresas.A.Id, $$"""{"version":2,"personas":[{"persona_id":"{{primero}}","resultado":{{SimitManual}}}]}""", ct);
        ok.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errorOk = await TableroAyudas.JsonAsync(ok, ct);
        errorOk.GetProperty("code").GetString().Should().Be("PERSONA_NO_EN_ERROR");
        errorOk.GetProperty("campo").GetString().Should().Be("personas[0].persona_id");
        var comoVehiculo = await Llenar(api, token, celda.Id, empresas.A.Id, Cuerpo(2, SimitManual), ct);
        comoVehiculo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await TableroAyudas.JsonAsync(comoVehiculo, ct)).GetProperty("campo").GetString().Should().Be("resultado");

        var respuesta = await Llenar(api, token, celda.Id, empresas.A.Id, $$"""{"version":2,"personas":[{"persona_id":"{{segundo}}","resultado":{{SimitManual}}}]}""", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var guardada = await CeldaAsync(empresas.A, celda.Id, ct);
        guardada.Estado.Should().Be(EstadoCelda.Completado);
        guardada.Origen.Should().Be(OrigenCelda.Manual);
        var personas = JsonDocument.Parse(guardada.Payload!).RootElement.GetProperty("personas");
        personas[0].GetRawText().Should().Be(antes, "el resultado de la persona que respondió por API no cambia");
        personas[1].GetProperty("persona_id").GetGuid().Should().Be(segundo);
        personas[1].GetProperty("estado").GetString().Should().Be("OK");
        personas[1].GetProperty("consulta").GetString().Should().Be("SIMIT");
        personas[1].GetProperty("resultado").GetProperty("cantidad_comparendos").GetInt32().Should().Be(1);
        personas[1].TryGetProperty("tipo_error", out _).Should().BeFalse();
        var intento = (await IntentosAsync(empresas.A, celda.Id, ct)).Should().ContainSingle().Subject;
        intento.PersonaId.Should().Be(segundo);
        intento.Disparador.Should().Be(DisparadorIntento.Manual);
        intento.UsuarioId.Should().Be(TableroAyudas.Usuario(token));

        var detalle = await TableroAyudas.JsonAsync(await empresas.A.Cliente().GetAsync(TableroAyudas.RutaDetalle(lote.Id, celda.VehiculoId, "SIMIT_PROPIETARIO"), ct), ct);
        detalle.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        detalle.GetProperty("personas").EnumerateArray().Select(p => p.GetProperty("estado").GetString()).Should().Equal("OK", "OK");
    }

    // Contrato para F06-H5 — GET del formulario: la celda, su versión y SOLO las personas en error, con el documento enmascarado.
    [Fact]
    public async Task Formulario_trae_solo_las_personas_en_error_enmascaradas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var (_, celda, _, segundo) = await CeldaDePersonasAsync(api, empresas.A, "TST795", ct);
        var token = TokenLlenado(api);

        var respuesta = await api.Cliente(token).GetAsync(Ruta(celda.Id, empresas.A.Id), ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        cuerpo.Should().NotContainAny(["9999900101", "9999900102"], "nunca el documento en claro");
        var formulario = JsonDocument.Parse(cuerpo).RootElement;
        formulario.GetProperty("id").GetGuid().Should().Be(celda.Id);
        formulario.GetProperty("fuente").GetString().Should().Be("SIMIT_PROPIETARIO");
        formulario.GetProperty("estado").GetString().Should().Be("ERROR");
        formulario.GetProperty("version").GetInt64().Should().Be(2);
        formulario.GetProperty("admite_manual").GetBoolean().Should().BeTrue();
        var persona = formulario.GetProperty("personas").EnumerateArray().Should().ContainSingle().Subject;
        persona.GetProperty("persona_id").GetGuid().Should().Be(segundo);
        persona.GetProperty("orden").GetInt32().Should().Be(2);
        persona.GetProperty("documento_enmascarado").GetString().Should().Be("****0102");
        persona.GetProperty("consulta").GetString().Should().Be("SIMIT");
        persona.GetProperty("tipo_error").GetString().Should().Be("TIMEOUT");
        (await api.Cliente(empresas.A.Token).GetAsync(Ruta(celda.Id, empresas.A.Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string TokenLlenado(ApiDePrueba api) =>
        api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer, Permisos.ConsultasLlenar], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

    private static Task<HttpResponseMessage> Llenar(ApiDePrueba api, string token, Guid consulta, Guid empresa, string cuerpo, CancellationToken ct) =>
        api.Cliente(token).PutAsync(Ruta(consulta, empresa), new StringContent(cuerpo, Encoding.UTF8, "application/json"), ct);

    private static async Task<CeldaConsulta> CeldaAsync(EmpresaDePrueba empresa, Guid id, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<CeldaConsulta>().AsNoTracking().SingleAsync(c => c.Id == id, ct);
    }

    private static async Task<List<IntentoConsulta>> IntentosAsync(EmpresaDePrueba empresa, Guid consulta, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<IntentoConsulta>().AsNoTracking().Where(i => i.ConsultaId == consulta).ToListAsync(ct);
    }

    /// <summary>
    /// Celda SIMIT_PROPIETARIO en ERROR <c>PERSONAS_CON_ERROR</c> con dos propietarios: el 1 respondió por API (OK), el 2 en
    /// ERROR TIMEOUT. Documentos ficticios cifrados con el protector del servicio.
    /// </summary>
    private static async Task<(LoteSembrado Lote, CeldaConsulta Celda, Guid Primero, Guid Segundo)> CeldaDePersonasAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, string placa, CancellationToken ct)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 2, vehiculos:
            [new(placa, [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitPropietario, EstadoCelda.Error, 2)])]);
        var celda = lote.Celda(placa, Fuente.SimitPropietario);
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var primero = new PersonaVehiculo
        {
            VehiculoId = celda.VehiculoId,
            Rol = RolPersona.Propietario,
            Orden = 1,
            DocTipo = "C",
            DocCifrado = protector.Proteger("9999900101"),
            Origen = OrigenDocumento.Proveedor,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        var segundo = new PersonaVehiculo
        {
            VehiculoId = celda.VehiculoId,
            Rol = RolPersona.Propietario,
            Orden = 2,
            DocTipo = "C",
            DocCifrado = protector.Proteger("9999900102"),
            Origen = OrigenDocumento.Proveedor,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        await using (var db = empresa.Db())
        {
            db.AddRange(primero, segundo);
            await db.SaveChangesAsync(ct);
        }

        var payload = $$"""
            {"personas":[
              {"persona_id":"{{primero.Id}}","orden":1,"consulta":"SIMIT","estado":"OK","resultado":{"semaforo":"VERDE","cantidad_comparendos":0,"comparendos":[]},"consultado_en":"2026-10-22T14:00:00+00:00"},
              {"persona_id":"{{segundo.Id}}","orden":2,"consulta":"SIMIT","estado":"ERROR","tipo_error":"TIMEOUT"}]}
            """;
        var conPayload = await TableroAyudas.PayloadAsync(empresa, celda, payload, ct, EstadoCelda.Error, EjecutorPorPersonas.PersonasConError);
        return (lote, conPayload, primero.Id, segundo.Id);
    }
}
