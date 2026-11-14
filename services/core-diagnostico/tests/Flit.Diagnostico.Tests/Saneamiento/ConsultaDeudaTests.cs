using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;
using VehiculoSembrado = Flit.Diagnostico.Tests.Tablero.VehiculoSembrado;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// HU #14576 AC36/AC37/AC33/AC34 (plan §6.5, SP-9, SP-11) — panel de deuda <c>GET /diagnosticos/{id}/vehiculos/{vid}/deuda</c>.
/// Las pruebas puras arman el panel con <see cref="ConsultaDeuda.Armar"/> sobre un contenido de reporte en memoria; las de HTTP
/// usan Postgres real (base efímera de <c>ApiDePrueba</c>) y prueban aislamiento, visibilidad, 409 y la forma del JSON.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Saneamiento.ConsultaDeudaTests
/// </code>
/// </example>
public sealed class ConsultaDeudaTests
{
    private static DictamenVehiculo DictamenBloqueado() => new()
    {
        VehiculoId = Guid.NewGuid(),
        Version = 1,
        Vigente = true,
        Veredicto = "BLOQUEADO",
        Alertamientos = """[{"codigo":"SIMIT_PROPIETARIO","texto":"El propietario tiene comparendos.","efecto":"BLOQUEA","fuente":"SIMIT_PROPIETARIO"},{"codigo":"SOAT_NO_VIGENTE","texto":"SOAT no vigente.","efecto":"BLOQUEA","fuente":"RUNT"}]""",
        CatalogoVersion = 2,
        RedactadoPor = "PLANTILLA",
        EstadoRedaccion = "LISTA",
        CreadoEn = DeudaDePrueba.Dato,
    };

    // AC36 — Con Análisis BLOQUEADO: SIMIT del propietario con comparendos, impuesto con deuda, SOAT no vigente, RTM vigente y
    // locatario en N/A sin documento (no leasing). Dictamen con motivos; una entrada por fuente con su estado; valor como texto.
    [Fact]
    public void Panel_con_analisis_trae_dictamen_y_un_estado_por_fuente()
    {
        var contenido = DeudaDePrueba.Contenido("TST701",
        [
            DeudaDePrueba.Completada("RUNT", DeudaDePrueba.RuntResultado()),
            DeudaDePrueba.Completada("SIMIT_VEHICULO", DeudaDePrueba.Simit(0, pazYSalvo: true)),
            DeudaDePrueba.Completada("SIMIT_PROPIETARIO", null, [DeudaDePrueba.Persona(1, "PROPIETARIO", DeudaDePrueba.DocumentoPropietario, DeudaDePrueba.Simit(2))]),
            DeudaDePrueba.NoAplica("SIMIT_LOCATARIO", "SIN_DOCUMENTO_LOCATARIO"),
            DeudaDePrueba.Completada("IMPUESTO_DEPARTAMENTAL", DeudaDePrueba.Impuesto(1250000)),
        ], DeudaDePrueba.DetalleRunt("TST701", ["NO VIGENTE"], ["SI"]));

        var panel = ConsultaDeuda.Armar(Guid.NewGuid(), contenido, ModoDiagnostico.ConAnalisis, DictamenBloqueado(), null, DeudaDePrueba.Hoy);

        panel.Modo.Should().Be("CON_ANALISIS");
        panel.Vehiculo.Placa.Should().Be("TST701");
        panel.Dictamen!.Veredicto.Should().Be("BLOQUEADO");
        panel.Dictamen.CatalogoVersion.Should().Be(2);
        panel.Dictamen.Motivos.Select(m => (m.Codigo, m.Efecto)).Should().Equal([("SIMIT_PROPIETARIO", "BLOQUEA"), ("SOAT_NO_VIGENTE", "BLOQUEA")]);
        panel.Fuentes.Select(f => (f.Fuente, f.Estado)).Should().Equal(
        [
            ("SIMIT_VEHICULO", "SIN_HALLAZGO"), ("SIMIT_PROPIETARIO", "CON_DEUDA"), ("SIMIT_LOCATARIO", "NO_APLICA"),
            ("IMPUESTO", "CON_DEUDA"), ("SOAT", "NO_VIGENTE"), ("RTM", "SIN_HALLAZGO"),
        ]);
        var propietario = panel.Fuentes.Single(f => f.Fuente == "SIMIT_PROPIETARIO");
        propietario.Persona!.Documento.Should().Be(DeudaDePrueba.DocumentoPropietario, "el documento completo, como el detalle de la celda (SP-11)");
        propietario.Comparendos!.Select(c => c.Valor).Should().AllBe("450000.00", "el valor va como texto, tal como llega");
        propietario.ConsultadoEn.Should().Be(DeudaDePrueba.Dato);
        panel.Fuentes.Single(f => f.Fuente == "SIMIT_LOCATARIO").MotivoNa.Should().Be("SIN_DOCUMENTO_LOCATARIO");
        var impuesto = panel.Fuentes.Single(f => f.Fuente == "IMPUESTO");
        impuesto.MontoTotal.Should().Be(1250000);
        impuesto.Vigencias!.Select(v => (v.Anio, v.Valor, v.Pagada)).Should().Equal([(2026, 1250000L, false), (2025, 180000L, true)]);
        panel.Fuentes.Single(f => f.Fuente == "SOAT").Vence.Should().Be("31/05/2026");
        panel.Saneamiento.Should().BeNull();
    }

    // AC36 — el JSON del panel no tiene nombre en ninguna profundidad (SP-11) ni total del SIMIT o total general (S17).
    [Fact]
    public void Json_del_panel_no_trae_nombre_ni_totales()
    {
        var contenido = DeudaDePrueba.Contenido("TST702",
        [
            DeudaDePrueba.Completada("SIMIT_VEHICULO", DeudaDePrueba.Simit(1)),
            DeudaDePrueba.Completada("SIMIT_PROPIETARIO", null, [DeudaDePrueba.Persona(1, "PROPIETARIO", DeudaDePrueba.DocumentoPropietario, DeudaDePrueba.Simit(1))]),
        ]);

        var json = JsonSerializer.SerializeToElement(ConsultaDeuda.Armar(Guid.NewGuid(), contenido, ModoDiagnostico.Simple, null, null, DeudaDePrueba.Hoy));

        DeudaDePrueba.Claves(json).Should().NotContain(["nombre", "total", "total_general", "total_por_pagar", "documento_enmascarado"]);
        json.GetRawText().Should().NotContainAny(["999999", "888888"]);
        json.GetProperty("dictamen").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("saneamiento").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // AC37 — modo Simple: dictamen null aunque haya fila (no debería), y fuentes con todas las consultadas.
    [Fact]
    public void Panel_simple_no_trae_dictamen()
    {
        var contenido = DeudaDePrueba.Contenido("TST703", [DeudaDePrueba.Completada("SIMIT_VEHICULO", DeudaDePrueba.Simit(0, pazYSalvo: false))]);

        var panel = ConsultaDeuda.Armar(Guid.NewGuid(), contenido, ModoDiagnostico.Simple, DictamenBloqueado(), null, DeudaDePrueba.Hoy);

        panel.Modo.Should().Be("SIMPLE");
        panel.Dictamen.Should().BeNull();
        panel.Fuentes.Single().Estado.Should().Be("CON_DEUDA", "sin paz y salvo es multa (mismo predicado que el dictamen)");
    }

    // C4 — leasing sin documento del locatario: SIMIT_LOCATARIO SIN_VERIFICAR; acuerdo con saldo es deuda; licencia suspendida no.
    [Fact]
    public void Leasing_sin_locatario_queda_sin_verificar_y_el_acuerdo_con_saldo_es_deuda()
    {
        var contenido = DeudaDePrueba.Contenido("TST704",
        [
            DeudaDePrueba.Completada("RUNT", DeudaDePrueba.RuntResultado(leasing: true, locatarios: 0)),
            DeudaDePrueba.Completada("SIMIT_VEHICULO", DeudaDePrueba.Simit(0, pazYSalvo: true, saldoAcuerdo: "300000")),
            DeudaDePrueba.NoAplica("SIMIT_LOCATARIO", "SIN_DOCUMENTO_LOCATARIO"),
        ], DeudaDePrueba.DetalleRunt("TST704", ["VIGENTE"], ["SI"]));

        var panel = ConsultaDeuda.Armar(Guid.NewGuid(), contenido, ModoDiagnostico.Simple, null, null, DeudaDePrueba.Hoy);

        panel.Fuentes.Single(f => f.Fuente == "SIMIT_LOCATARIO").Estado.Should().Be("SIN_VERIFICAR");
        var vehiculo = panel.Fuentes.Single(f => f.Fuente == "SIMIT_VEHICULO");
        vehiculo.Estado.Should().Be("CON_DEUDA");
        vehiculo.AcuerdosPago!.Single().SaldoPendiente.Should().Be("300000");
        panel.Fuentes.Single(f => f.Fuente == "SOAT").Estado.Should().Be("SIN_HALLAZGO");
    }

    // C4 — remolque: SOAT y RTM no aplican; RUNT en ERROR deja SOAT y RTM sin verificar; SIMIT en ERROR, sin verificar.
    [Fact]
    public void Remolque_no_exige_soat_ni_rtm_y_error_queda_sin_verificar()
    {
        var remolque = DeudaDePrueba.Contenido("R12345",
            [DeudaDePrueba.Completada("RUNT", DeudaDePrueba.RuntResultado())],
            DeudaDePrueba.DetalleRunt("R12345", ["NO VIGENTE"], ["NO"], clase: "REMOLQUE"));
        var conError = DeudaDePrueba.Contenido("TST705", [DeudaDePrueba.ConError("RUNT"), DeudaDePrueba.ConError("SIMIT_VEHICULO")]);

        var deRemolque = ConsultaDeuda.Armar(Guid.NewGuid(), remolque, ModoDiagnostico.Simple, null, null, DeudaDePrueba.Hoy);
        var deError = ConsultaDeuda.Armar(Guid.NewGuid(), conError, ModoDiagnostico.Simple, null, null, DeudaDePrueba.Hoy);

        deRemolque.Fuentes.Select(f => (f.Fuente, f.Estado)).Should().Equal([("SOAT", "NO_APLICA"), ("RTM", "NO_APLICA")]);
        deError.Fuentes.Select(f => (f.Fuente, f.Estado)).Should().Equal([("SIMIT_VEHICULO", "SIN_VERIFICAR"), ("SOAT", "SIN_VERIFICAR"), ("RTM", "SIN_VERIFICAR")]);
    }

    // AC36 — los comparendos llegan con el tope de 100 de la proyección y la bandera comparendos_truncados.
    [Fact]
    public void Comparendos_con_tope_y_bandera_de_truncado()
    {
        var contenido = DeudaDePrueba.Contenido("TST706", [DeudaDePrueba.Completada("SIMIT_VEHICULO", DeudaDePrueba.Simit(130))]);

        var simit = ConsultaDeuda.Armar(Guid.NewGuid(), contenido, ModoDiagnostico.Simple, null, null, DeudaDePrueba.Hoy).Fuentes.Single();

        simit.Comparendos.Should().HaveCount(100);
        simit.ComparendosTruncados.Should().BeTrue();
    }

    // AC37/AC36 por HTTP — lote Simple terminado: 200, sin dictamen, documento completo, sin «nombre» aunque el payload lo traiga,
    // sin caché. Un vehículo con una celda PENDIENTE: 409 VEHICULO_NO_TERMINADO.
    [Fact]
    public async Task Http_panel_terminado_y_409_si_no_termino()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos:
        [
            new VehiculoSembrado("TST711", [new(Fuente.SimitVehiculo, EstadoCelda.Completado, 1), new(Fuente.SimitPropietario, EstadoCelda.Completado, 1)]),
            new VehiculoSembrado("TST712", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.SimitPropietario, EstadoCelda.Completado, 1)]),
        ]);
        var vehiculo = lote.Vehiculos.Single(v => v.Placa == "TST711");
        var persona = await DeudaDePrueba.PersonaAsync(api, empresa, vehiculo.Id, RolPersona.Propietario, 1, DeudaDePrueba.DocumentoPropietario, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST711", Fuente.SimitVehiculo),
            """{"semaforo":"ROJO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0711","fecha":"2026-09-30","valor":"468500.50","estado":"PENDIENTE","organismo":"Organismo de prueba"}]}""", ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST711", Fuente.SimitPropietario), DeudaDePrueba.PayloadPersonas(
            (persona.Id, 1, """{"semaforo":"VERDE","cantidad_comparendos":0,"comparendos":[],"paz_y_salvo":true,"nombre":"PERSONA FICTICIA ZQX"}""")), ct);
        var token = DeudaDePrueba.TokenVer(api, empresa.Id);

        var respuesta = await api.Cliente(token).GetAsync(DeudaDePrueba.RutaPanel(lote.Id, vehiculo.Id), ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue("trae el documento completo");
        var json = JsonElement.Parse(cuerpo);
        json.GetProperty("modo").GetString().Should().Be("SIMPLE");
        json.GetProperty("dictamen").ValueKind.Should().Be(JsonValueKind.Null);
        var fuentes = json.GetProperty("fuentes").EnumerateArray().ToList();
        fuentes.Select(f => f.GetProperty("fuente").GetString()).Should().Equal(["SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SOAT", "RTM"], "RUNT sin celda: SOAT y RTM sin verificar");
        fuentes[0].GetProperty("estado").GetString().Should().Be("CON_DEUDA");
        fuentes[0].GetProperty("comparendos")[0].GetProperty("valor").ValueKind.Should().Be(JsonValueKind.String);
        fuentes[0].GetProperty("comparendos")[0].GetProperty("valor").GetString().Should().Be("468500.50");
        fuentes[1].GetProperty("estado").GetString().Should().Be("SIN_HALLAZGO");
        fuentes[1].GetProperty("persona").GetProperty("documento").GetString().Should().Be(DeudaDePrueba.DocumentoPropietario);
        DeudaDePrueba.Claves(json).Should().NotContain("nombre");
        cuerpo.Should().NotContain("PERSONA FICTICIA ZQX");

        var enCurso = await api.Cliente(token).GetAsync(DeudaDePrueba.RutaPanel(lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST712").Id), ct);
        enCurso.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await DeudaDePrueba.JsonAsync(enCurso, ct)).GetProperty("code").GetString().Should().Be("VEHICULO_NO_TERMINADO");
    }

    // AC33/AC34/SP-9 — 404 antes que 409: lote de otra compañía, vehículo de otro lote de la misma compañía (aunque no esté
    // terminado) y lote de otro usuario con PROPIOS, todos 404 NO_ENCONTRADO sin datos; sin diagnosticos.ver, 403.
    [Fact]
    public async Task Http_aislamiento_y_visibilidad_responden_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var creador = DeudaDePrueba.TokenVer(api, empresas.A.Id);
        var otro = DeudaDePrueba.TokenVer(api, empresas.A.Id);
        var deB = await TableroAyudas.SembrarAsync(empresas.B, ct, 
            vehiculos: [new VehiculoSembrado("TST721", [new(Fuente.SimitVehiculo, EstadoCelda.Completado, 1)])]);
        var deA = await TableroAyudas.SembrarAsync(empresas.A, ct, creador: TableroAyudas.Usuario(creador), 
            vehiculos: [new VehiculoSembrado("TST722", [new(Fuente.SimitVehiculo, EstadoCelda.Completado, 1)])]);
        var otroLoteA = await TableroAyudas.SembrarAsync(empresas.A, ct, creador: TableroAyudas.Usuario(creador), 
            vehiculos: [new VehiculoSembrado("TST723", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)])]);

        var ajena = await api.Cliente(creador).GetAsync(DeudaDePrueba.RutaPanel(deB.Id, deB.Vehiculos.Single().Id), ct);
        var cuerpoAjena = await ajena.Content.ReadAsStringAsync(ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, cuerpoAjena);
        JsonElement.Parse(cuerpoAjena).GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
        cuerpoAjena.Should().NotContain("TST721");

        var cruzado = await api.Cliente(creador).GetAsync(DeudaDePrueba.RutaPanel(deA.Id, otroLoteA.Vehiculos.Single().Id), ct);
        cruzado.StatusCode.Should().Be(HttpStatusCode.NotFound, "el vehículo es de otro lote: 404 antes que 409");

        (await api.Cliente(creador).GetAsync(DeudaDePrueba.RutaPanel(deA.Id, deA.Vehiculos.Single().Id), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await DeudaDePrueba.PropiosAsync(empresas.A, ct);
        var propiosAjeno = await api.Cliente(otro).GetAsync(DeudaDePrueba.RutaPanel(deA.Id, deA.Vehiculos.Single().Id), ct);
        propiosAjeno.StatusCode.Should().Be(HttpStatusCode.NotFound, "PROPIOS: el lote es de otro usuario");

        var sinPermiso = api.Token(empresa: empresas.A.Id, permisos: [Permisos.ConfiguracionVer], roles: ["usuario_diagnostico"]);
        (await api.Cliente(sinPermiso).GetAsync(DeudaDePrueba.RutaPanel(deA.Id, deA.Vehiculos.Single().Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
