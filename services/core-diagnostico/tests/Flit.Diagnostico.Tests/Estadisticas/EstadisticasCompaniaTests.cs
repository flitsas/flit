using System.Net;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Configuracion;
using Flit.Diagnostico.Tests.Saneamiento;
using FluentAssertions;
using Xunit;
using static Flit.Diagnostico.Tests.Estadisticas.EstadisticasDePrueba;

namespace Flit.Diagnostico.Tests.Estadisticas;

/// <summary>
/// HU #14579, BE-1 — <c>GET /estadisticas</c> (E1) de la compañía del token (contrato-api §1; SP-5, SP-6/O9, SP-11): cohorte de lotes
/// creados en el rango y visibles para el usuario, serie con ceros, los 5 estados de saneamiento, top vacío, visibilidad PROPIOS, el
/// SuperAdmin acotado a su compañía, tiempos sin intentos MANUAL, rango y parámetros inválidos, y <c>Cache-Control: no-store</c>.
/// PostgreSQL real (las agregaciones son SQL).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EstadisticasCompaniaTests
/// </code>
/// </example>
public sealed class EstadisticasCompaniaTests
{
    // AC «indicadores de la compañía en el periodo» — 3 lotes en el rango (2 express, 1 masivo) y 1 fuera del rango.
    [Fact]
    public async Task BE1_AC1_indicadores_de_la_compania_en_el_periodo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var a = DosEmpresas.En(api).A;
        var express1 = await LoteAsync(a, Dia(-5), ct, express: true, placas: ["EST101"]);
        await LoteAsync(a, Dia(-2), ct, express: true, modo: ModoDiagnostico.ConAnalisis, placas: ["EST102"]);
        var masivo = await LoteAsync(a, Dia(-2), ct, placas: ["EST103", "EST104", "EST101"],
            celdas: [(Fuente.Runt, EstadoCelda.Completado), (Fuente.SimitVehiculo, EstadoCelda.Error)]);
        await LoteAsync(a, Dia(-30), ct, placas: ["EST199"]);
        await DictamenAsync(a, express1.Vehiculos[0].Id, "VIABLE", ct);
        await DictamenAsync(a, masivo.Vehiculos[0].Id, "BLOQUEADO", ct);
        await SaneamientoDePrueba.SembrarAsync(a, masivo.Id, masivo.Vehiculos[1].Id, EstadoSaneamiento.EnGestion, ct);
        await DerivacionAsync(a, masivo.Id, masivo.Vehiculos[2].Id, ct);

        var respuesta = await a.Cliente().GetAsync(Ruta(Hoy.AddDays(-6), Hoy), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Headers.CacheControl?.NoStore.Should().BeTrue();
        var dto = await JsonAsync(respuesta, ct);
        dto.GetProperty("ambito").GetProperty("tipo").GetString().Should().Be("EMPRESA");
        dto.GetProperty("ambito").GetProperty("empresa_id").GetGuid().Should().Be(a.Id);
        dto.GetProperty("ambito").GetProperty("empresa_nombre").GetString().Should().Be("Empresa de prueba");
        dto.GetProperty("periodo").GetProperty("granularidad").GetString().Should().Be("DIA");
        var resumen = dto.GetProperty("resumen");
        resumen.GetProperty("diagnosticos").GetInt32().Should().Be(3);
        resumen.GetProperty("diagnosticos_express").GetInt32().Should().Be(2);
        resumen.GetProperty("diagnosticos_masivos").GetInt32().Should().Be(1);
        resumen.GetProperty("vehiculos").GetInt32().Should().Be(5);
        resumen.GetProperty("vehiculos_unicos").GetInt32().Should().Be(4, "EST101 está en dos lotes de la misma compañía");
        resumen.GetProperty("consultas").GetInt32().Should().Be(8);
        resumen.GetProperty("consultas_exitosas").GetInt32().Should().Be(5);
        resumen.GetProperty("consultas_con_error").GetInt32().Should().Be(3);
        resumen.GetProperty("tasa_exito").GetDouble().Should().Be(0.625);
        resumen.GetProperty("saneamientos_solicitados").GetInt32().Should().Be(1);
        var serie = dto.GetProperty("serie").EnumerateArray().ToList();
        serie.Should().HaveCount(7, "una cubeta por día del rango, con ceros");
        serie.Select(c => c.GetProperty("diagnosticos").GetInt32()).Should().Equal(0, 1, 0, 0, 2, 0, 0);
        dto.GetProperty("saneamientos").EnumerateArray().Select(s => (s.GetProperty("estado").GetString(), s.GetProperty("cantidad").GetInt32()))
            .Should().Equal(("SOLICITADO", 0), ("EN_GESTION", 1), ("SANEADO", 0), ("NO_PROCEDE", 0), ("ANULADO", 0));
        dto.GetProperty("veredictos").GetProperty("viables").GetInt32().Should().Be(1);
        dto.GetProperty("veredictos").GetProperty("bloqueados").GetInt32().Should().Be(1);
        dto.GetProperty("veredictos").GetProperty("sin_dictamen").GetInt32().Should().Be(3);
        dto.GetProperty("tipos_analisis").GetProperty("con_analisis").GetInt32().Should().Be(1);
        dto.GetProperty("derivaciones").GetProperty("en_curso").GetInt32().Should().Be(1);
        dto.GetProperty("fuentes").EnumerateArray().Select(f => f.GetProperty("fuente").GetString()).Should().Equal("RUNT", "SIMIT_VEHICULO");
        dto.GetProperty("top_empresas").GetArrayLength().Should().Be(0);
    }

    // AC «visibilidad PROPIOS» (SP-6, O9) — U1 cuenta solo su lote; el admin de la compañía, los dos.
    [Fact]
    public async Task BE1_AC2_visibilidad_PROPIOS()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var a = DosEmpresas.En(api).A;
        await ConfiguracionOperativaTests.SembrarAsync(a, new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios }, ct);
        var (u1, u2) = (Guid.NewGuid(), Guid.NewGuid());
        var deU1 = await LoteAsync(a, Dia(-1), ct, creador: u1, placas: ["EST111"]);
        var deU2 = await LoteAsync(a, Dia(-1), ct, creador: u2, placas: ["EST112", "EST113"]);
        await SaneamientoDePrueba.SembrarAsync(a, deU2.Id, deU2.Vehiculos[0].Id, EstadoSaneamiento.Solicitado, ct);

        var paraU1 = await JsonAsync(await api.Cliente(TokenUsuario(a, u1)).GetAsync(Ruta(Hoy.AddDays(-3), Hoy), ct), ct);
        var paraAdmin = await JsonAsync(await api.Cliente(TokenAdmin(a)).GetAsync(Ruta(Hoy.AddDays(-3), Hoy), ct), ct);

        paraU1.GetProperty("resumen").GetProperty("diagnosticos").GetInt32().Should().Be(1);
        paraU1.GetProperty("resumen").GetProperty("vehiculos").GetInt32().Should().Be(1);
        paraU1.GetProperty("resumen").GetProperty("saneamientos_solicitados").GetInt32().Should().Be(0, "el saneamiento es de un lote de U2");
        paraAdmin.GetProperty("resumen").GetProperty("diagnosticos").GetInt32().Should().Be(2);
        paraAdmin.GetProperty("resumen").GetProperty("vehiculos").GetInt32().Should().Be(3);
        paraAdmin.GetProperty("resumen").GetProperty("saneamientos_solicitados").GetInt32().Should().Be(1);
        deU1.Id.Should().NotBe(deU2.Id);
    }

    // AC «el SuperAdmin en la ruta de compañía ve solo su compañía» (SP-5).
    [Fact]
    public async Task BE1_AC3_SuperAdmin_en_la_ruta_de_compania_ve_solo_su_compania()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (DosEmpresas.En(api).A, DosEmpresas.En(api).B);
        await LoteAsync(a, Dia(-1), ct, placas: ["EST121"]);
        await LoteAsync(b, Dia(-1), ct, placas: ["EST122", "EST123"]);
        await LoteAsync(b, Dia(-1), ct, placas: ["EST124"]);

        var respuesta = await api.Cliente(api.Token(empresa: a.Id, roles: ["SuperAdmin"], permisos: [])).GetAsync(Ruta(Hoy.AddDays(-2), Hoy), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var dto = await JsonAsync(respuesta, ct);
        dto.GetProperty("resumen").GetProperty("diagnosticos").GetInt32().Should().Be(1);
        dto.GetProperty("resumen").GetProperty("vehiculos").GetInt32().Should().Be(1);
        dto.GetRawText().Should().NotContain(b.Id.ToString());
    }

    // AC «tiempos sin intentos manuales y tasa de éxito» — AUTOMATICO 1000 y 3000 ms y uno MANUAL de 9000 ms en RUNT: promedio 2000.
    [Fact]
    public async Task BE1_AC4_tiempos_sin_intentos_manuales()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var a = DosEmpresas.En(api).A;
        var lote = await LoteAsync(a, Dia(0), ct, placas: ["EST131"]);
        var celda = lote.Celdas.Single();
        await IntentoAsync(a, celda, 1000, DisparadorIntento.Auto, ct);
        await IntentoAsync(a, celda, 3000, DisparadorIntento.Auto, ct);
        await IntentoAsync(a, celda, 9000, DisparadorIntento.Manual, ct);

        var dto = await JsonAsync(await a.Cliente().GetAsync(Ruta(Hoy, Hoy), ct), ct);

        var runt = dto.GetProperty("fuentes").EnumerateArray().Single(f => f.GetProperty("fuente").GetString() == "RUNT");
        runt.GetProperty("tiempo_promedio_ms").GetInt64().Should().Be(2000);
        runt.GetProperty("tasa_exito").GetDouble().Should().Be(1.0);
        dto.GetProperty("resumen").GetProperty("tiempo_promedio_consulta_ms").GetInt64().Should().Be(2000);
    }

    // AC «rango y parámetros inválidos» en E1 (por HTTP, con el cuerpo { code, campo }).
    [Theory]
    [InlineData("hasta={hoy}", "FILTRO_INVALIDO")]
    [InlineData("desde={hace366}&hasta={hoy}", "RANGO_INVALIDO")]
    [InlineData("desde={hoy}&hasta={manana}", "RANGO_INVALIDO")]
    [InlineData("desde={hoy}&hasta={hoy}&empresa=0192f1a2-0000-7000-8000-000000000001", "FILTRO_INVALIDO")]
    [InlineData("desde={hoy}&hasta={hoy}&documento=1020304050", "FILTRO_INVALIDO")]
    public async Task BE1_AC5_rango_y_parametros_invalidos_en_E1(string consulta, string codigo)
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var ct = TestContext.Current.CancellationToken;
        var url = $"{RutaCompania}?{consulta.Replace("{hoy}", Fecha(Hoy), StringComparison.Ordinal).Replace("{hace366}", Fecha(Hoy.AddDays(-366)), StringComparison.Ordinal).Replace("{manana}", Fecha(Hoy.AddDays(1)), StringComparison.Ordinal)}";

        var respuesta = await api.Cliente(api.Token(permisos: [Permisos.DiagnosticosVer])).GetAsync(url, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await JsonAsync(respuesta, ct);
        cuerpo.GetProperty("code").GetString().Should().Be(codigo);
        cuerpo.GetRawText().Should().NotContain("1020304050");
    }

    // Sin diagnosticos.ver: 403.
    [Fact]
    public async Task BE1_AC5_sin_diagnosticos_ver_responde_403()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Cliente(api.Token(roles: ["usuario_diagnostico"], permisos: [Permisos.ReportesVer])).GetAsync(Ruta(Hoy, Hoy), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().NotContain("resumen", "el 403 no trae datos (la autorización responde sin cuerpo)");
    }
}
