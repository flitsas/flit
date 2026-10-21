using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13743 (F04-H1) — <c>GET /diagnosticos/{id}/grilla?desde=</c>: solo lo cambiado desde la versión del cliente, una fila
/// por vehículo con las columnas del alcance agrupadas, avance por vehículo (R-7), fecha y hora del dato reutilizado (D-10),
/// DTO de cliente sin origen, usuario manual ni documentos (R-20) y estimado frente a lo consumido con
/// <c>estimado_consultas=AL_FINALIZAR</c> (R-11b). PostgreSQL real (<see cref="ApiDePrueba"/>); los lotes se siembran directo.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Tablero.GrillaTests
/// </code>
/// </example>
public sealed class GrillaTests
{
    private static readonly string[] Prohibidos = ["Origen", "UsuarioManual", "Documento", "DocTipo", "DocCifrado", "Payload"];

    // AC1 (F04-C3) — lote en versión 10, dos celdas cambian a 11 y 12: desde 10 llegan solo esas dos y la versión 12.
    [Fact]
    public async Task Desde_version_devuelve_solo_celdas_cambiadas()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 12, vehiculos:
        [
            new("TST101", [new(Fuente.Runt, EstadoCelda.Completado, 4), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 11)]),
            new("TST102", [new(Fuente.Runt, EstadoCelda.Completado, 10), new(Fuente.SimitVehiculo, EstadoCelda.Error, 12)]),
            new("TST103", [new(Fuente.Runt, EstadoCelda.Completado, 7), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 9)]),
        ]);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 10), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var grilla = await Tablero.JsonAsync(respuesta, ct);
        grilla.GetProperty("version").GetInt64().Should().Be(12);
        grilla.GetProperty("desde").GetInt64().Should().Be(10);
        Tablero.CeldasDe(grilla).Select(c => c.GetProperty("id").GetGuid()).Should().BeEquivalentTo(
            [lote.Celda("TST101", Fuente.SimitVehiculo).Id, lote.Celda("TST102", Fuente.SimitVehiculo).Id]);
        grilla.GetProperty("vehiculos").EnumerateArray().Select(v => v.GetProperty("identificador").GetString())
            .Should().Equal(["TST101", "TST102"], "solo los vehículos con celdas cambiadas, en su orden");
        grilla.GetProperty("estado").GetString().Should().Be("EN_PROCESO", "el estado global va siempre");
        grilla.GetProperty("avance").GetProperty("vehiculos").GetInt32().Should().Be(3, "el avance va siempre, con todo el lote");

        var completa = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 0), ct), ct);
        Tablero.CeldasDe(completa).Should().HaveCount(6, "con desde=0 llega todo");
        var sinDesde = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct), ct);
        Tablero.CeldasDe(sinDesde).Should().HaveCount(6, "sin desde es desde=0");
        var alDia = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 12), ct), ct);
        alDia.GetProperty("vehiculos").GetArrayLength().Should().Be(0, "nada cambió desde la versión actual");
        alDia.GetProperty("version").GetInt64().Should().Be(12);

        var negativa = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: -1), ct);
        negativa.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Tablero.JsonAsync(negativa, ct)).GetProperty("code").GetString().Should().Be("DESDE_INVALIDO");
    }

    // AC5 (F04-C11, R-20) — el DTO de cliente no tiene origen, usuario manual, payload ni documentos (reflexión) y la respuesta
    // de una celda llenada a mano con un vehículo con documento de locatario tampoco los lleva.
    [Fact]
    public async Task Dto_de_cliente_no_expone_origen_usuario_manual_ni_documentos()
    {
        var propiedades = PropiedadesDe(typeof(GrillaDto)).ToList();
        propiedades.Should().NotBeEmpty();
        propiedades.Select(p => p.Name).Should().NotContain(n => Prohibidos.Any(x => n.Contains(x, StringComparison.OrdinalIgnoreCase)));
        propiedades.Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
            .Should().NotContain(n => n.Contains("origen") || n.Contains("usuario_manual") || n.Contains("doc") || n.Contains("payload"));

        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, manual: true, vehiculos:
            [new("TST111", [new(Fuente.Runt, EstadoCelda.Completado, 1)], ConLocatario: true)]);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        cuerpo.Should().NotContainAny(["origen", "MANUAL", "usuario_manual", "documento", "payload", "99999123", "locatario_doc"]);
        cuerpo.Should().NotContain(lote.Celdas.Single().UsuarioManualId!.Value.ToString());
        Tablero.CeldasDe(JsonDocument.Parse(cuerpo).RootElement).Single().GetProperty("estado").GetString().Should().Be("COMPLETADO");
    }

    // AC5 (F04-C10, D-10) — la celda reutilizada trae la fecha y hora del dato original.
    [Fact]
    public async Task Celda_reutilizada_trae_fecha_y_hora_del_dato()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var original = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST121", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST121", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var obtenidoEn = new DateTimeOffset(2026, 10, 21, 8, 15, 30, TimeSpan.Zero);
        await using (var db = empresa.Db())
        {
            var origen = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == original.Celdas.Single().Id, ct);
            origen.DatoObtenidoEn = obtenidoEn;
            var reutilizada = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == lote.Celdas.Single().Id, ct);
            reutilizada.ReutilizadaDeId = origen.Id;
            reutilizada.DatoObtenidoEn = obtenidoEn;
            await db.SaveChangesAsync(ct);
        }

        var grilla = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct), ct);

        var celda = Tablero.CeldasDe(grilla).Single();
        celda.GetProperty("reutilizada").GetBoolean().Should().BeTrue();
        celda.GetProperty("dato_obtenido_en").GetDateTimeOffset().Should().Be(obtenidoEn);
        var propia = Tablero.CeldasDe(await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(original.Id), ct), ct)).Single();
        propia.GetProperty("reutilizada").GetBoolean().Should().BeFalse();
    }

    // AC2 (F04-C1, F04-C8) — una fila por vehículo, columnas del alcance agrupadas y el primer vehículo terminado sin esperar al lote.
    [Fact]
    public async Task Vehiculo_terminado_sin_esperar_al_lote()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 5, alcance: [Fuente.Runt, Fuente.SimitVehiculo, Fuente.RuntLocatario], vehiculos:
        [
            new("TST131", [new(Fuente.Runt, EstadoCelda.Completado, 2), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 4), new(Fuente.RuntLocatario, EstadoCelda.Na, 2)]),
            new("TST132", [new(Fuente.Runt, EstadoCelda.Completado, 3), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 3), new(Fuente.RuntLocatario, EstadoCelda.Na, 3)]),
        ]);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 0), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var grilla = await Tablero.JsonAsync(respuesta, ct);
        var filas = grilla.GetProperty("vehiculos").EnumerateArray().ToList();
        filas.Select(f => f.GetProperty("identificador").GetString()).Should().Equal("TST131", "TST132");
        filas.Select(f => f.GetProperty("terminado").GetBoolean()).Should().Equal(true, false);
        filas[0].GetProperty("celdas").EnumerateArray().Select(c => c.GetProperty("fuente").GetString())
            .Should().Equal(["RUNT", "SIMIT_VEHICULO", "RUNT_LOCATARIO"], "en el orden del alcance");
        filas[0].GetProperty("celdas")[2].GetProperty("motivo_na").GetString().Should().Be("SIN_DOCUMENTO_LOCATARIO");
        var columnas = grilla.GetProperty("columnas");
        Cadenas(columnas, "por_vehiculo").Should().Equal("RUNT", "SIMIT_VEHICULO");
        Cadenas(columnas, "por_propietario").Should().BeEmpty();
        Cadenas(columnas, "por_locatario").Should().Equal("RUNT_LOCATARIO");
        grilla.GetProperty("avance").GetProperty("vehiculos").GetInt32().Should().Be(2);
        grilla.GetProperty("avance").GetProperty("terminados").GetInt32().Should().Be(1);
        grilla.GetProperty("estado").GetString().Should().Be("EN_PROCESO");
        grilla.GetProperty("id").GetGuid().Should().Be(lote.Id);
    }

    // AC8 (F04-C13) — AL_FINALIZAR y lote terminado: estimado por fuente frente a lo consumido, sin contar las reutilizadas.
    [Fact]
    public async Task Al_finalizar_muestra_estimado_vs_consumido()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Completado, estimado: "AL_FINALIZAR",
            alcance: [Fuente.Runt, Fuente.RuntLocatario], vehiculos:
            [
                new("TST141", [new(Fuente.Runt, EstadoCelda.Completado, 2), new(Fuente.RuntLocatario, EstadoCelda.Completado, 3)], ConLocatario: true),
                new("TST142", [new(Fuente.Runt, EstadoCelda.Completado, 2), new(Fuente.RuntLocatario, EstadoCelda.Na, 2)]),
            ]);
        var runt141 = lote.Celda("TST141", Fuente.Runt);
        await Tablero.IntentoAsync(empresa, runt141, desdeCache: false, ct);
        await Tablero.IntentoAsync(empresa, runt141, desdeCache: false, ct);
        await Tablero.IntentoAsync(empresa, lote.Celda("TST142", Fuente.Runt), desdeCache: true, ct);
        await Tablero.IntentoAsync(empresa, lote.Celda("TST141", Fuente.RuntLocatario), desdeCache: false, ct);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var estimado = (await Tablero.JsonAsync(respuesta, ct)).GetProperty("estimado_consultas").EnumerateArray()
            .Select(e => $"{e.GetProperty("fuente").GetString()} {e.GetProperty("estimadas").GetInt32()} {e.GetProperty("consumidas").GetInt32()}");
        estimado.Should().Equal(["RUNT 2 2", "RUNT_LOCATARIO 1 1"], "la reutilizada (desde_cache) no consume");
    }

    // AC8 — sin AL_FINALIZAR, o con el lote abierto, la respuesta no trae estimado.
    [Fact]
    public async Task No_mostrar_omite_estimado()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var celdas = new CeldaSembrada[] { new(Fuente.Runt, EstadoCelda.Completado, 1) };
        var noMostrar = await Tablero.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Completado, estimado: "NO_MOSTRAR", vehiculos: [new("TST151", celdas)]);
        var antes = await Tablero.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Completado, estimado: "ANTES_DE_LANZAR", vehiculos: [new("TST152", celdas)]);
        var abierto = await Tablero.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.EnProceso, estimado: "AL_FINALIZAR",
            vehiculos: [new("TST153", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);

        foreach (var lote in new[] { noMostrar, antes, abierto })
        {
            var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Tablero.JsonAsync(respuesta, ct)).TryGetProperty("estimado_consultas", out _).Should().BeFalse(lote.Cabecera.Configuracion);
        }
    }

    // La acción «reprocesar» se publica en la sesión solo con diagnostico.consultas.reprocesar (el endpoint es de #13746).
    [Fact]
    public async Task Sesion_publica_reprocesar_solo_con_el_permiso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var ruta = Flit.Diagnostico.Api.Program.Prefijo + "/sesion";

        var conPermiso = await Tablero.JsonAsync(await api.Cliente(api.Token(permisos: [Permisos.DiagnosticosVer, Permisos.ConsultasReprocesar])).GetAsync(ruta, ct), ct);
        var sinPermiso = await Tablero.JsonAsync(await api.Cliente(api.Token(permisos: [Permisos.DiagnosticosVer])).GetAsync(ruta, ct), ct);

        conPermiso.GetProperty("capacidades").GetProperty("reprocesarConsultas").GetBoolean().Should().BeTrue();
        sinPermiso.GetProperty("capacidades").GetProperty("reprocesarConsultas").GetBoolean().Should().BeFalse();
        Permisos.Todos.Should().Contain(Permisos.ConsultasReprocesar);
    }

    private static IEnumerable<string?> Cadenas(JsonElement objeto, string propiedad) =>
        objeto.GetProperty(propiedad).EnumerateArray().Select(e => e.GetString());

    /// <summary>Propiedades del DTO y de todos los tipos del servicio que cuelgan de él (listas incluidas).</summary>
    private static IEnumerable<PropertyInfo> PropiedadesDe(Type raiz)
    {
        var vistos = new HashSet<Type>();
        var pendientes = new Queue<Type>([raiz]);
        while (pendientes.Count > 0)
        {
            var tipo = pendientes.Dequeue();
            if (!vistos.Add(tipo))
                continue;
            foreach (var propiedad in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                yield return propiedad;
                var t = Nullable.GetUnderlyingType(propiedad.PropertyType) ?? propiedad.PropertyType;
                foreach (var candidato in t.IsGenericType ? t.GetGenericArguments() : [t])
                {
                    if (candidato.Assembly == typeof(GrillaDto).Assembly)
                        pendientes.Enqueue(candidato);
                }
            }
        }
    }
}
