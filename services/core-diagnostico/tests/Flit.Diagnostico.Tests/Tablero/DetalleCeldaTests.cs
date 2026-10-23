using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13798 (F06-H1) — <c>GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}</c>: resultado normalizado de cada
/// fuente (F06-C1), personas con el documento enmascarado (<c>****</c> + 4 últimos, F06-C2), avalúo «sin valor comercial»,
/// misma forma para una celda manual y una de API sin origen, usuario manual, proveedor ni transacción (F06-C3/C12) y
/// aislamiento: otra compañía y PROPIOS de otro usuario 404, sin permiso 403 (F06-C4). PostgreSQL real
/// (<see cref="ApiDePrueba"/>); lotes y personas sembrados directo con documentos ficticios cifrados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Tablero.DetalleCeldaTests
/// </code>
/// </example>
public sealed class DetalleCeldaTests
{
    private const string PayloadRunt =
        """{"placa":"TST401","vin":"9ZZPRUEBA00000401","servicio":"PARTICULAR","clase":"AUTOMOVIL","organismo":"STRIA PRUEBA","servicio_que_resolvio":"INTEMPO_SIMULADO","id_transaccion":"TX-SECRETA-401","propietarios":[{"tipo_documento":"C","tipo_propiedad":"PROPIA"},{"tipo_documento":"N","tipo_propiedad":"PROPIA"}],"locatarios":0}""";

    private const string PayloadAvaluo =
        """{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{"fuente":"fasecolda","estado":"OK","valor":48500000,"muestras":3,"fecha":"2026-10-22"},{"fuente":"mercado_libre","estado":"SIN_DATOS","fecha":"2026-10-22"}]}""";

    private const string PayloadSinValor =
        """{"sin_valor_comercial":true,"fuentes":[{"fuente":"fasecolda","estado":"SIN_DATOS","fecha":"2026-10-22"},{"fuente":"mercado_libre","estado":"SIN_DATOS","fecha":"2026-10-22"}]}""";

    private static readonly string[] ClavesProhibidas =
        ["origen", "usuario", "proveedor", "servicio_que_resolvio", "transaccion", "payload", "persona_id", "cifrado", "nombre"];

    // AC1 (F06-C1) — cada fuente devuelve su resultado normalizado: RUNT, SIMIT_VEHICULO (con tope de comparendos), AVALUO y
    // SIMIT_PROPIETARIO con dos propietarios, una entrada por persona con su resultado. Fuente desconocida: 400.
    [Fact]
    public async Task Devuelve_resultado_normalizado_de_cada_fuente()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos:
        [
            new("TST401", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1),
                new(Fuente.Avaluo, EstadoCelda.Pendiente, 1), new(Fuente.SimitPropietario, EstadoCelda.Pendiente, 1),
                new(Fuente.SimitLocatario, EstadoCelda.Na, 1)]),
            new("TST402", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]),
        ]);
        var vehiculo = lote.Vehiculos.Single(v => v.Placa == "TST401").Id;
        await Tablero.PayloadAsync(empresa, lote.Celda("TST401", Fuente.Runt), PayloadRunt, ct);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST401", Fuente.SimitVehiculo), Simit(2, "ROJO"), ct);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST401", Fuente.Avaluo), PayloadAvaluo, ct);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST402", Fuente.SimitVehiculo), Simit(150, "ROJO"), ct);
        var p1 = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 1, "C", "1010101401", ct);
        var p2 = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 2, "C", "2020202402", ct);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST401", Fuente.SimitPropietario), Personas(
            (p1.Id, 1, "SIMIT", "OK", Simit(1, "AMARILLO"), null), (p2.Id, 2, "SIMIT", "OK", Simit(0, "VERDE"), null)), ct);
        var cliente = empresa.Cliente();

        var runt = await DetalleAsync(cliente, lote.Id, vehiculo, "RUNT", ct);
        runt.GetProperty("fuente").GetString().Should().Be("RUNT");
        runt.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        runt.GetProperty("dato_obtenido_en").ValueKind.Should().Be(JsonValueKind.String);
        runt.GetProperty("personas").ValueKind.Should().Be(JsonValueKind.Null, "RUNT es del vehículo");
        var resultadoRunt = runt.GetProperty("resultado");
        resultadoRunt.GetProperty("placa").GetString().Should().Be("TST401");
        resultadoRunt.GetProperty("clase").GetString().Should().Be("AUTOMOVIL");
        resultadoRunt.GetProperty("propietarios").EnumerateArray().Select(p => p.GetProperty("tipo_documento").GetString()).Should().Equal(["C", "N"]);

        var simit = (await DetalleAsync(cliente, lote.Id, vehiculo, "SIMIT_VEHICULO", ct)).GetProperty("resultado");
        simit.GetProperty("semaforo").GetString().Should().Be("ROJO");
        simit.GetProperty("cantidad_comparendos").GetInt32().Should().Be(2);
        simit.GetProperty("comparendos_truncados").GetBoolean().Should().BeFalse();
        var comparendo = simit.GetProperty("comparendos")[0];
        comparendo.GetProperty("numero").GetString().Should().Be("C-0001");
        comparendo.GetProperty("valor").GetString().Should().Be("500000");
        comparendo.GetProperty("estado").GetString().Should().Be("PENDIENTE");

        var largo = (await DetalleAsync(cliente, lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST402").Id, "SIMIT_VEHICULO", ct)).GetProperty("resultado");
        largo.GetProperty("cantidad_comparendos").GetInt32().Should().Be(150, "la cantidad es la real");
        largo.GetProperty("comparendos").GetArrayLength().Should().Be(DetalleCelda.TopeComparendos, "la lista se corta en el tope");
        largo.GetProperty("comparendos_truncados").GetBoolean().Should().BeTrue();

        var avaluo = (await DetalleAsync(cliente, lote.Id, vehiculo, "AVALUO", ct)).GetProperty("resultado");
        avaluo.GetProperty("sin_valor_comercial").GetBoolean().Should().BeFalse();
        avaluo.GetProperty("valor_sugerido").GetInt64().Should().Be(48_500_000);
        avaluo.GetProperty("fuente_principal").GetString().Should().Be("fasecolda");
        avaluo.GetProperty("fuentes").EnumerateArray().Select(f => f.GetProperty("estado").GetString()).Should().Equal(["OK", "SIN_DATOS"]);

        var propietarios = await DetalleAsync(cliente, lote.Id, vehiculo, "SIMIT_PROPIETARIO", ct);
        propietarios.GetProperty("resultado").ValueKind.Should().Be(JsonValueKind.Null, "el resultado va por persona");
        var personas = propietarios.GetProperty("personas").EnumerateArray().ToList();
        personas.Should().HaveCount(2, "una entrada por propietario");
        personas.Select(p => p.GetProperty("orden").GetInt32()).Should().Equal([1, 2]);
        personas.Select(p => p.GetProperty("estado").GetString()).Should().Equal(["OK", "OK"]);
        personas.Select(p => p.GetProperty("resultado").GetProperty("cantidad_comparendos").GetInt32()).Should().Equal([1, 0]);
        personas[0].GetProperty("resultado").GetProperty("semaforo").GetString().Should().Be("AMARILLO");

        var na = await DetalleAsync(cliente, lote.Id, vehiculo, "SIMIT_LOCATARIO", ct);
        na.GetProperty("estado").GetString().Should().Be("NA");
        na.GetProperty("motivo_na").GetString().Should().Be("SIN_DOCUMENTO_LOCATARIO");
        na.GetProperty("personas").GetArrayLength().Should().Be(0, "el vehículo no tiene locatario");

        var desconocida = await cliente.GetAsync(Tablero.RutaDetalle(lote.Id, vehiculo, "FASECOLDA"), ct);
        desconocida.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Tablero.JsonAsync(desconocida, ct)).GetProperty("code").GetString().Should().Be(DetalleCelda.CodigoFuenteInvalida);
    }

    // AC2 (F06-C2) y AC1 — RUNT_LOCATARIO con locatario declarado: «****» + 4 últimos y declarado_por_cliente=true; RUNT_PROPIETARIO
    // con un natural en OK y un NIT en ERROR (resultado parcial). Nunca el documento completo.
    [Fact]
    public async Task Fuente_de_persona_trae_cada_persona_enmascarada()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos:
            [new("TST421", [new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1), new(Fuente.RuntPropietario, EstadoCelda.Pendiente, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, 1, "C", "1012345678", ct, OrigenDocumento.DeclaradoCliente);
        var natural = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 1, "C", "79111222", ct);
        var juridica = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, 2, "NIT", "900123456-7", ct);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST421", Fuente.RuntLocatario), Personas(
            (locatario.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[{"categoria":"B1","estado":"VIGENTE","vigente_hasta":"2030-01-01"}]}""", null)), ct);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST421", Fuente.RuntPropietario), Personas(
            (natural.Id, 1, "RUNT_PERSONA", "OK", """{"inscrito":false,"licencias":[]}""", null),
            (juridica.Id, 2, "RUES", "ERROR", null, "TIMEOUT")), ct, EstadoCelda.Error, "PERSONAS_CON_ERROR");
        var cliente = empresa.Cliente();

        var respuesta = await cliente.GetAsync(Tablero.RutaDetalle(lote.Id, vehiculo, "RUNT_LOCATARIO"), ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        cuerpo.Should().NotContainAny(["1012345678", "012345678", "12345678"], "el documento nunca va completo");
        var persona = JsonDocument.Parse(cuerpo).RootElement.GetProperty("personas").EnumerateArray().Single();
        persona.GetProperty("documento_enmascarado").GetString().Should().Be("****5678");
        persona.GetProperty("declarado_por_cliente").GetBoolean().Should().BeTrue();
        persona.GetProperty("rol").GetString().Should().Be("LOCATARIO");
        persona.GetProperty("tipo_documento").GetString().Should().Be("C");
        persona.GetProperty("consulta").GetString().Should().Be("RUNT_PERSONA");
        persona.GetProperty("resultado").GetProperty("licencias")[0].GetProperty("categoria").GetString().Should().Be("B1");

        var propietarios = await cliente.GetAsync(Tablero.RutaDetalle(lote.Id, vehiculo, "RUNT_PROPIETARIO"), ct);
        var cuerpoPropietarios = await propietarios.Content.ReadAsStringAsync(ct);
        propietarios.StatusCode.Should().Be(HttpStatusCode.OK, cuerpoPropietarios);
        cuerpoPropietarios.Should().NotContainAny(["79111222", "900123456", natural.Id.ToString(), juridica.Id.ToString()]);
        var detalle = JsonDocument.Parse(cuerpoPropietarios).RootElement;
        detalle.GetProperty("estado").GetString().Should().Be("ERROR");
        detalle.GetProperty("tipo_error").GetString().Should().Be("PERSONAS_CON_ERROR");
        var personas = detalle.GetProperty("personas").EnumerateArray().ToList();
        personas.Select(p => p.GetProperty("documento_enmascarado").GetString()).Should().Equal(["****1222", "****4567"]);
        personas.Select(p => p.GetProperty("declarado_por_cliente").GetBoolean()).Should().Equal([false, false]);
        personas.Select(p => p.GetProperty("estado").GetString()).Should().Equal(["OK", "ERROR"]);
        personas[0].GetProperty("resultado").GetProperty("inscrito").GetBoolean().Should().BeFalse();
        personas[1].GetProperty("tipo_error").GetString().Should().Be("TIMEOUT");
        personas[1].GetProperty("consulta").GetString().Should().Be("RUES");
        personas[1].GetProperty("resultado").ValueKind.Should().Be(JsonValueKind.Null);

        EnmascaradorDocumento.Enmascarar("123").Should().Be("****", "un documento corto no se muestra");
        EnmascaradorDocumento.Enmascarar("1234").Should().Be("****", "con cuatro caracteres o menos se vería completo");
        EnmascaradorDocumento.Enmascarar(null).Should().Be("****");
    }

    // AC3 (F06-C1, Q4 = b) — AVALUO COMPLETADO sin valor: sin_valor_comercial=true, sin valor sugerido ni fuente principal.
    [Fact]
    public async Task Avaluo_sin_valor_se_lee_sin_valor_comercial()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST431", [new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)])]);
        await Tablero.PayloadAsync(empresa, lote.Celdas.Single(), PayloadSinValor, ct);

        var detalle = await DetalleAsync(empresa.Cliente(), lote.Id, lote.Vehiculos.Single().Id, "AVALUO", ct);

        detalle.GetProperty("estado").GetString().Should().Be("COMPLETADO", "sin valor no es error");
        var resultado = detalle.GetProperty("resultado");
        resultado.GetProperty("sin_valor_comercial").GetBoolean().Should().BeTrue();
        resultado.GetProperty("valor_sugerido").ValueKind.Should().Be(JsonValueKind.Null);
        resultado.GetProperty("fuente_principal").ValueKind.Should().Be(JsonValueKind.Null);
        resultado.GetProperty("fuentes").GetArrayLength().Should().Be(2);
    }

    // AC4 (F06-C3, F06-C12) — una celda de API y otra llenada a mano tienen exactamente las mismas claves y ninguna trae origen,
    // usuario manual, proveedor ni transacción.
    [Fact]
    public async Task Celda_manual_y_celda_de_API_tienen_la_misma_forma()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos:
        [
            new("TST441", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1)]),
            new("TST442", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1)]),
        ]);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST441", Fuente.SimitVehiculo), Simit(2, "ROJO"), ct);
        var manual = await Tablero.PayloadAsync(empresa, lote.Celda("TST442", Fuente.SimitVehiculo), Simit(2, "ROJO"), ct, manual: true);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST441", Fuente.Runt), PayloadRunt, ct);
        var runtManual = await Tablero.PayloadAsync(empresa, lote.Celda("TST442", Fuente.Runt), PayloadRunt.Replace("TST401", "TST442", StringComparison.Ordinal), ct, manual: true);
        var cliente = empresa.Cliente();

        foreach (var fuente in new[] { "SIMIT_VEHICULO", "RUNT" })
        {
            var deApi = await cliente.GetAsync(Tablero.RutaDetalle(lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST441").Id, fuente), ct);
            var aMano = await cliente.GetAsync(Tablero.RutaDetalle(lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST442").Id, fuente), ct);
            var cuerpoApi = await deApi.Content.ReadAsStringAsync(ct);
            var cuerpoManual = await aMano.Content.ReadAsStringAsync(ct);
            deApi.StatusCode.Should().Be(HttpStatusCode.OK, cuerpoApi);
            aMano.StatusCode.Should().Be(HttpStatusCode.OK, cuerpoManual);

            Claves(JsonDocument.Parse(cuerpoManual).RootElement).Should().Equal(Claves(JsonDocument.Parse(cuerpoApi).RootElement), $"{fuente}: misma forma");
            Claves(JsonDocument.Parse(cuerpoManual).RootElement).Should().NotContain(c => ClavesProhibidas.Any(p => c.Contains(p, StringComparison.Ordinal)));
            cuerpoManual.Should().NotContainAny(["MANUAL", "API", manual.UsuarioManualId!.Value.ToString(), runtManual.UsuarioManualId!.Value.ToString()]);
        }
    }

    // AC4 (F06-C3, F06-C12) — por reflexión, el DTO (y sus tipos de resultado) no tiene origen, usuario, proveedor, transacción,
    // payload ni documentos completos; y la respuesta no deja pasar campos del proveedor ni crudos que traiga el payload.
    [Fact]
    public async Task Nunca_incluye_payload_crudo_proveedor_ni_transaccion()
    {
        var nombres = PropiedadesDe(typeof(DetalleCeldaDto)).Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name).ToList();
        nombres.Should().Contain(["resultado", "personas", "documento_enmascarado", "sin_valor_comercial", "comparendos", "licencias", "estado_matricula"],
            "la reflexión recorre los tipos de resultado");
        nombres.Should().NotContain(n => ClavesProhibidas.Any(p => n.Contains(p, StringComparison.OrdinalIgnoreCase)));

        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos:
            [new("TST451", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)])]);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST451", Fuente.Runt), PayloadRunt, ct);
        await Tablero.PayloadAsync(empresa, lote.Celda("TST451", Fuente.SimitVehiculo),
            """{"semaforo":"VERDE","cantidad_comparendos":0,"comparendos":[],"respuesta_cruda":"CRUDO-SECRETO","transaccion_proveedor_id":"TX-SIMIT-9","nombre_infractor":"NOMBRE FICTICIO"}""", ct);
        var vehiculo = lote.Vehiculos.Single().Id;

        foreach (var fuente in new[] { "RUNT", "SIMIT_VEHICULO" })
        {
            var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaDetalle(lote.Id, vehiculo, fuente), ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
            cuerpo.Should().NotContainAny(["INTEMPO", "TX-SECRETA-401", "servicio_que_resolvio", "id_transaccion", "CRUDO-SECRETO", "TX-SIMIT-9", "NOMBRE FICTICIO", "respuesta_cruda"]);
        }
    }

    // AC5 (F06-C4) — el detalle de un diagnóstico de la compañía B pedido por A responde 404 sin datos; un vehículo de otro lote
    // o una fuente sin celda, también 404.
    [Fact]
    public async Task Otra_compania_responde_404()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var deB = await Tablero.SembrarAsync(empresas.B, ct, nombre: "Lote secreto de B", vehiculos: [new("TST461", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        await Tablero.PayloadAsync(empresas.B, deB.Celdas.Single(), PayloadRunt.Replace("TST401", "TST461", StringComparison.Ordinal), ct);
        var propio = await Tablero.SembrarAsync(empresas.A, ct, vehiculos: [new("TST462", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var otroPropio = await Tablero.SembrarAsync(empresas.A, ct, vehiculos: [new("TST463", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var cliente = empresas.A.Cliente();

        var respuestas = new[]
        {
            await cliente.GetAsync(Tablero.RutaDetalle(deB.Id, deB.Vehiculos.Single().Id, "RUNT"), ct),
            await cliente.GetAsync(Tablero.RutaDetalle(propio.Id, deB.Vehiculos.Single().Id, "RUNT"), ct),
            await cliente.GetAsync(Tablero.RutaDetalle(propio.Id, otroPropio.Vehiculos.Single().Id, "RUNT"), ct),
            await cliente.GetAsync(Tablero.RutaDetalle(propio.Id, propio.Vehiculos.Single().Id, "AVALUO"), ct),
        };

        foreach (var respuesta in respuestas)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{respuesta.RequestMessage!.RequestUri}: {cuerpo}");
            JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
            cuerpo.Should().NotContainAny(["Lote secreto de B", "TST461", deB.Celdas.Single().Id.ToString(), empresas.B.Id.ToString()]);
        }

        (await cliente.GetAsync(Tablero.RutaDetalle(propio.Id, propio.Vehiculos.Single().Id, "RUNT"), ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "A sí ve su propia celda");
    }

    // AC5 (F06-C4, R-21) — con PROPIOS el detalle de un lote de otro usuario responde 404 (el creador y el admin sí lo ven); sin
    // diagnostico.diagnosticos.ver, 403.
    [Fact]
    public async Task Propios_de_otro_usuario_404_y_sin_permiso_403()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios }, ct);
        var creador = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        var otro = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        var admin = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar], roles: ["admin_diagnostico"]);
        var sinPermiso = api.Token(empresa: empresa.Id, permisos: [Permisos.ConfiguracionVer]);
        var lote = await Tablero.SembrarAsync(empresa, ct, creador: Tablero.Usuario(creador), vehiculos: [new("TST471", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        await Tablero.PayloadAsync(empresa, lote.Celdas.Single(), PayloadRunt.Replace("TST401", "TST471", StringComparison.Ordinal), ct);
        var ruta = Tablero.RutaDetalle(lote.Id, lote.Vehiculos.Single().Id, "RUNT");

        var ajena = await api.Cliente(otro).GetAsync(ruta, ct);
        var cuerpo = await ajena.Content.ReadAsStringAsync(ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, cuerpo);
        cuerpo.Should().NotContain("TST471");
        (await api.Cliente(creador).GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK, "el creador ve su celda");
        (await api.Cliente(admin).GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK, "el admin ve todas");
        (await api.Cliente(sinPermiso).GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // HU #13864 AC1 (F09-C9, Q1 = a, R-20) — celda del impuesto COMPLETADO con 350.000 pendientes de 2026 y 2025 pagada: el detalle
    // trae monto_total y las dos vigencias por año ascendente, con solo las claves del contrato; ni el proveedor inventado ni la
    // transacción ni el origen ni el departamento del payload llegan al JSON (fuga), y tampoco el servicio del intento.
    [Fact]
    public async Task Impuesto_completado_devuelve_monto_y_vigencias_sin_proveedor_ni_transaccion()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.ImpuestoDepartamental], vehiculos:
            [new("TST481", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)])]);
        var celda = lote.Celda("TST481", Fuente.ImpuestoDepartamental);
        await Tablero.PayloadAsync(empresa, celda, ImpuestoDePrueba.ConVigencias, ct);
        await Tablero.IntentoAsync(empresa, celda, desdeCache: false, ct);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaDetalle(lote.Id, lote.Vehiculos.Single().Id, "IMPUESTO_DEPARTAMENTAL"), ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var detalle = JsonDocument.Parse(cuerpo).RootElement;
        detalle.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        detalle.GetProperty("personas").ValueKind.Should().Be(JsonValueKind.Null, "es una fuente del vehículo");
        var resultado = detalle.GetProperty("resultado");
        resultado.ValueKind.Should().Be(JsonValueKind.Object, cuerpo);
        resultado.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["monto_total", "vigencias"]);
        resultado.GetProperty("monto_total").GetInt64().Should().Be(350000, "pesos enteros");
        var vigencias = resultado.GetProperty("vigencias").EnumerateArray().ToList();
        vigencias.Select(v => v.GetProperty("anio").GetInt32()).Should().Equal([2025, 2026], "por año ascendente");
        vigencias.Select(v => v.GetProperty("valor").GetInt64()).Should().Equal([180000L, 350000L]);
        vigencias.Select(v => v.GetProperty("pagada").GetBoolean()).Should().Equal([true, false]);
        vigencias.Should().AllSatisfy(v => v.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["anio", "valor", "pagada"]));
        Claves(detalle).Should().NotContain(c => ClavesProhibidas.Any(p => c.Contains(p, StringComparison.Ordinal)) || c.Contains("departamento", StringComparison.Ordinal));
        cuerpo.Should().NotContainAny([ImpuestoDePrueba.ProveedorFicticio, ImpuestoDePrueba.TransaccionFicticia, "INTEMPO", "departamento_codigo", "id_transaccion", "\"API\""]);
    }

    // HU #13864 AC3 (F09-C9) — celdas del impuesto en N/A: el detalle trae el motivo DEPARTAMENTO_NO_SOPORTADO o SERVICIO_PUBLICO,
    // sin resultado ni personas (el texto lo pone el frontend con el mismo catálogo de motivos).
    [Fact]
    public async Task Impuesto_NA_devuelve_el_motivo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.ImpuestoDepartamental], vehiculos:
        [
            new("TST482", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)]),
            new("TST483", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.ImpuestoDepartamental, EstadoCelda.Pendiente, 1)]),
        ]);
        await ImpuestoDePrueba.NoAplicaAsync(empresa, lote.Celda("TST482", Fuente.ImpuestoDepartamental), MotivoNa.DepartamentoNoSoportado, ct);
        await ImpuestoDePrueba.NoAplicaAsync(empresa, lote.Celda("TST483", Fuente.ImpuestoDepartamental), MotivoNa.ServicioPublico, ct);

        foreach (var (placa, motivo) in new[] { ("TST482", "DEPARTAMENTO_NO_SOPORTADO"), ("TST483", "SERVICIO_PUBLICO") })
        {
            var detalle = await DetalleAsync(empresa.Cliente(), lote.Id, lote.Vehiculos.Single(v => v.Placa == placa).Id, "IMPUESTO_DEPARTAMENTAL", ct);
            detalle.GetProperty("estado").GetString().Should().Be("NA");
            detalle.GetProperty("motivo_na").GetString().Should().Be(motivo);
            detalle.GetProperty("resultado").ValueKind.Should().Be(JsonValueKind.Null, "N/A no tiene dato");
            detalle.GetProperty("personas").ValueKind.Should().Be(JsonValueKind.Null);
            detalle.GetProperty("dato_obtenido_en").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    private static async Task<JsonElement> DetalleAsync(HttpClient cliente, Guid lote, Guid vehiculo, string fuente, CancellationToken ct)
    {
        var respuesta = await cliente.GetAsync(Tablero.RutaDetalle(lote, vehiculo, fuente), ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, $"{fuente}: {cuerpo}");
        return JsonDocument.Parse(cuerpo).RootElement.Clone();
    }

    private static async Task<PersonaVehiculo> PersonaAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid vehiculo, RolPersona rol, int orden, string tipo, string numero, CancellationToken ct,
        OrigenDocumento origen = OrigenDocumento.Proveedor)
    {
        var persona = new PersonaVehiculo
        {
            VehiculoId = vehiculo,
            Rol = rol,
            Orden = orden,
            DocTipo = tipo,
            DocCifrado = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Proteger(numero),
            Origen = origen,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        await using var db = empresa.Db();
        db.Add(persona);
        await db.SaveChangesAsync(ct);
        return persona;
    }

    /// <summary>Payload SIMIT normalizado con <paramref name="cantidad"/> comparendos ficticios.</summary>
    private static string Simit(int cantidad, string semaforo) => JsonSerializer.Serialize(new
    {
        semaforo,
        cantidad_comparendos = cantidad,
        comparendos = Enumerable.Range(1, cantidad).Select(i => new
        {
            numero = $"C-{i:D4}",
            fecha = "2026-01-02",
            valor = "500000",
            estado = "PENDIENTE",
            organismo = "ORGANISMO PRUEBA",
        }),
    });

    /// <summary>Payload de una celda de persona con la forma de <c>EjecutorPorPersonas</c>.</summary>
    private static string Personas(params (Guid Id, int Orden, string Consulta, string Estado, string? Resultado, string? TipoError)[] personas) =>
        "{\"personas\":[" + string.Join(",", personas.Select(p =>
            $$"""{"persona_id":"{{p.Id}}","orden":{{p.Orden}},"consulta":"{{p.Consulta}}","estado":"{{p.Estado}}"{{(p.Resultado is null ? string.Empty : ",\"resultado\":" + p.Resultado)}}{{(p.TipoError is null ? string.Empty : $",\"tipo_error\":\"{p.TipoError}\"")}}{{(p.Estado == "OK" ? ",\"consultado_en\":\"2026-10-22T15:00:00+00:00\"" : string.Empty)}}}""")) + "]}";

    /// <summary>Rutas de todas las claves del JSON (objetos y elementos de listas), ordenadas.</summary>
    private static List<string> Claves(JsonElement raiz)
    {
        var claves = new SortedSet<string>(StringComparer.Ordinal);
        void Recorrer(JsonElement e, string ruta)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    claves.Add($"{ruta}/{p.Name}");
                    Recorrer(p.Value, $"{ruta}/{p.Name}");
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                    Recorrer(item, $"{ruta}[]");
            }
        }

        Recorrer(raiz, string.Empty);
        return [.. claves];
    }

    /// <summary>Propiedades del DTO, de los tipos que cuelgan de él y de los tipos derivados de resultado (<c>JsonDerivedType</c>).</summary>
    private static IEnumerable<PropertyInfo> PropiedadesDe(Type raiz)
    {
        var vistos = new HashSet<Type>();
        var pendientes = new Queue<Type>([raiz]);
        while (pendientes.Count > 0)
        {
            var tipo = pendientes.Dequeue();
            if (!vistos.Add(tipo))
                continue;
            foreach (var derivado in tipo.GetCustomAttributes<JsonDerivedTypeAttribute>())
                pendientes.Enqueue(derivado.DerivedType);
            foreach (var propiedad in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (propiedad.DeclaringType == typeof(object) || propiedad.Name == "EqualityContract")
                    continue;
                yield return propiedad;
                var t = Nullable.GetUnderlyingType(propiedad.PropertyType) ?? propiedad.PropertyType;
                foreach (var candidato in t.IsGenericType ? t.GetGenericArguments() : [t])
                {
                    if (candidato.Assembly == typeof(DetalleCeldaDto).Assembly)
                        pendientes.Enqueue(candidato);
                }
            }
        }
    }
}
