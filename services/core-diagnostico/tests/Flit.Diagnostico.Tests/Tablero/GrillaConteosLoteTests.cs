using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #14563 (B1 sin versión de fila, B2, B3): veredicto por fila y conteos del lote en la grilla, bloque <c>lote</c> con el
/// creador, el trámite y la fecha, y modo/tipo de trámite en el listado de backoffice. PostgreSQL real; placas TST###.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~GrillaConteosLoteTests
/// </code>
/// </example>
public sealed class GrillaConteosLoteTests
{
    // AC1 — Con análisis: veredicto VIABLE, BLOQUEADO o null por fila y conteos con todo el lote.
    [Fact]
    public async Task HU14563_AC1_Fila_con_veredicto_y_grilla_con_conteos()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 9, modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_STANDARD", vehiculos:
        [
            new("TST151", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
            new("TST152", [new(Fuente.Runt, EstadoCelda.Completado, 2)]),
            new("TST153", [new(Fuente.Runt, EstadoCelda.Completado, 3)]),
            new("TST154", [new(Fuente.Runt, EstadoCelda.Completado, 4), new(Fuente.SimitVehiculo, EstadoCelda.Error, 5)]),
        ]);
        await DictamenAsync(empresa, lote.Vehiculos[0].Id, "VIABLE", ct);
        await DictamenAsync(empresa, lote.Vehiculos[1].Id, "BLOQUEADO", ct);

        var grilla = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 0), ct), ct);

        var veredictos = grilla.GetProperty("vehiculos").EnumerateArray().ToDictionary(
            v => v.GetProperty("identificador").GetString()!, v => v.GetProperty("veredicto").ValueKind == JsonValueKind.Null ? null : v.GetProperty("veredicto").GetString());
        veredictos.Should().BeEquivalentTo(new Dictionary<string, string?> { ["TST151"] = "VIABLE", ["TST152"] = "BLOQUEADO", ["TST153"] = null, ["TST154"] = null });
        var conteos = grilla.GetProperty("conteos");
        conteos.GetProperty("vehiculos").GetInt32().Should().Be(4);
        conteos.GetProperty("terminados").GetInt32().Should().Be(3);
        conteos.GetProperty("con_error").GetInt32().Should().Be(1);
        conteos.GetProperty("con_deuda").GetInt32().Should().Be(0);
        conteos.GetProperty("bloqueados").GetInt32().Should().Be(1);
        conteos.GetProperty("listos_tramite").GetInt32().Should().Be(1, "solo el terminado, VIABLE, sin trámite ni ítem vivo");

        // AC4 (parte de conteos) — en el delta los conteos se calculan con todo el lote.
        var delta = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 4), ct), ct);
        delta.GetProperty("vehiculos").GetArrayLength().Should().Be(1);
        delta.GetProperty("conteos").GetRawText().Should().Be(conteos.GetRawText());
    }

    // AC2 — Simple: veredicto null en todas las filas, bloqueados 0 y listos = terminados sin trámite ni ítem vivo.
    [Fact]
    public async Task HU14563_AC2_Modo_simple_sin_veredicto_ni_bloqueados()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, modo: ModoDiagnostico.Simple, vehiculos:
        [
            new("TST161", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
            new("TST162", [new(Fuente.Runt, EstadoCelda.Error, 1)]),
        ]);

        var grilla = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct), ct);

        grilla.GetProperty("vehiculos").EnumerateArray().Should().OnlyContain(v => v.GetProperty("veredicto").ValueKind == JsonValueKind.Null);
        grilla.GetProperty("conteos").GetProperty("bloqueados").GetInt32().Should().Be(0);
        grilla.GetProperty("conteos").GetProperty("listos_tramite").GetInt32().Should().Be(1);
        grilla.GetProperty("conteos").GetProperty("con_error").GetInt32().Should().Be(1);
    }

    // AC10 — bloque lote con modo, tipo de trámite con nombre, fecha, express y creador con su nombre, también en el delta.
    [Fact]
    public async Task HU14563_AC10_Bloque_lote_con_creador_tramite_y_fecha()
    {
        var identidad = new UsuariosIdentidadDePrueba();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<IResolutorNombresUsuario>(identidad.Resolutor()));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var creador = Guid.NewGuid();
        identidad.Nombres[creador] = "Ana Prueba";
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 3, creador: creador, modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_STANDARD",
            vehiculos: [new("TST171", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        foreach (var desde in new long[] { 0, 3 })
        {
            var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
            var bloque = (await Tablero.JsonAsync(respuesta, ct)).GetProperty("lote");
            bloque.GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
            bloque.GetProperty("tipo_tramite").GetProperty("codigo").GetString().Should().Be("TRASPASO_STANDARD");
            bloque.GetProperty("tipo_tramite").GetProperty("nombre").GetString().Should().Be("TRASPASO");
            bloque.GetProperty("es_express").GetBoolean().Should().BeFalse();
            bloque.GetProperty("creado_en").GetDateTimeOffset().Should().BeCloseTo(lote.Cabecera.CreadoEn, TimeSpan.FromSeconds(1));
            bloque.GetProperty("usuario_creador").GetProperty("id").GetGuid().Should().Be(creador);
            bloque.GetProperty("usuario_creador").GetProperty("nombre").GetString().Should().Be("Ana Prueba");
        }
    }

    // AC11 — Identidad no responde: 200 con usuario_creador.nombre null y el resto de la grilla completa.
    [Fact]
    public async Task HU14563_AC11_Resolutor_caido_deja_el_nombre_null()
    {
        var identidad = new UsuariosIdentidadDePrueba { Error = new RpcException(new Status(StatusCode.Unavailable, "identidad caída")) };
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<IResolutorNombresUsuario>(identidad.Resolutor()));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, modo: ModoDiagnostico.Simple, vehiculos: [new("TST181", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var grilla = await Tablero.JsonAsync(respuesta, ct);
        grilla.GetProperty("lote").GetProperty("usuario_creador").GetProperty("nombre").ValueKind.Should().Be(JsonValueKind.Null);
        grilla.GetProperty("lote").GetProperty("tipo_tramite").ValueKind.Should().Be(JsonValueKind.Null, "Simple no tiene trámite");
        grilla.GetProperty("vehiculos").GetArrayLength().Should().Be(1);
    }

    // AC12 — backoffice: cada lote con modo, tipo_tramite (código y nombre) y es_express; una llamada al catálogo por página.
    [Fact]
    public async Task HU14563_AC12_Backoffice_lista_modo_y_tramite()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var conAnalisis = await Tablero.SembrarAsync(empresa, ct, nombre: "Con análisis", modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_STANDARD",
            vehiculos: [new("TST191", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var otro = await Tablero.SembrarAsync(empresa, ct, nombre: "Con análisis 2", modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_UNILATERAL",
            vehiculos: [new("TST192", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var simple = await Tablero.SembrarAsync(empresa, ct, nombre: "Simple", modo: ModoDiagnostico.Simple,
            vehiculos: [new("TST193", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var antes = api.Catalogos.Llamadas;
        var token = api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

        var respuesta = await api.Cliente(token).GetAsync($"{Program.Prefijo}/backoffice/diagnosticos?empresa={empresa.Id}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var lotes = (await Tablero.JsonAsync(respuesta, ct)).GetProperty("diagnosticos").EnumerateArray().ToDictionary(d => d.GetProperty("id").GetGuid());
        lotes[conAnalisis.Id].GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
        lotes[conAnalisis.Id].GetProperty("tipo_tramite").GetProperty("codigo").GetString().Should().Be("TRASPASO_STANDARD");
        lotes[conAnalisis.Id].GetProperty("tipo_tramite").GetProperty("nombre").GetString().Should().Be("TRASPASO");
        lotes[otro.Id].GetProperty("tipo_tramite").GetProperty("nombre").GetString().Should().Be("TRASPASO UNILATERAL");
        lotes[conAnalisis.Id].GetProperty("es_express").GetBoolean().Should().BeFalse();
        lotes[simple.Id].GetProperty("modo").GetString().Should().Be("SIMPLE");
        lotes[simple.Id].GetProperty("tipo_tramite").ValueKind.Should().Be(JsonValueKind.Null);
        (api.Catalogos.Llamadas - antes).Should().BeLessThanOrEqualTo(1, "los nombres se resuelven con una sola llamada por página, no por lote");
    }

    private static async Task DictamenAsync(EmpresaDePrueba empresa, Guid vehiculo, string veredicto, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(new DictamenVehiculo
        {
            VehiculoId = vehiculo,
            Version = 1,
            Vigente = true,
            Veredicto = veredicto,
            Alertamientos = "[]",
            CatalogoVersion = 1,
            RedactadoPor = "PLANTILLA",
            EstadoRedaccion = "PENDIENTE",
            CreadoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
