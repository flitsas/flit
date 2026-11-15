using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Saneamiento;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-2 (R1; SP-8, SP-9, SP-16; contrato-api §3 y §4) — reporte en pantalla de la compañía: documentos completos de
/// <c>personas_vehiculo</c>, una fila de <c>accesos_reporte</c> por página (fail-closed), consultas sin rastro del soporte y los
/// reportes de saneamientos y derivaciones. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReportesPantallaTests
/// </code>
/// </example>
public sealed class ReportesPantallaTests
{
    // Vehículos con documento completo: propietarios en orden, locatario declarado, «No disponible» si el cifrado no se lee.
    [Fact]
    public async Task BE2_vehiculos_trae_documentos_completos_y_no_disponible_si_no_se_descifra()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa,
        [
            new("RPT101", Vin: "9ZZRPT00000000101", Propietarios: [new("CC", "1020304050"), new("NIT", "900123456")],
                Locatario: new("CC", "5566778899"), LocatarioDeclarado: true, Veredicto: "VIABLE"),
        ], ct, nombre: "Lote con documentos");
        await ReportesDePrueba.LoteAsync(empresa, [new("RPT102", CifradoIlegible: true)], ct, nombre: "Lote ilegible", express: true);

        var respuesta = await api.Cliente(ReportesDePrueba.Token(api, empresa.Id)).GetAsync(ReportesDePrueba.Ruta("vehiculos", "pagina=1&tamano=25"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue("SP-14: la pantalla con PII no se cachea");
        var pagina = await ReportesDePrueba.JsonAsync(respuesta, ct);
        pagina.GetProperty("tipo").GetString().Should().Be("vehiculos");
        pagina.GetProperty("ambito").GetString().Should().Be("COMPANIA");
        pagina.GetProperty("pagina").GetInt32().Should().Be(1);
        pagina.GetProperty("tamano").GetInt32().Should().Be(25);
        pagina.GetProperty("total").GetInt32().Should().Be(2);
        var filas = ReportesDePrueba.Filas(pagina).ToDictionary(f => f.GetProperty("placa").GetString()!);
        var conDocs = filas["RPT101"];
        conDocs.GetProperty("propietarios_documentos").GetString().Should().Be("CC 1020304050; NIT 900123456");
        conDocs.GetProperty("locatario_documento").GetString().Should().Be("CC 5566778899");
        conDocs.GetProperty("locatario_origen").GetString().Should().Be("DECLARADO");
        conDocs.GetProperty("vin").GetString().Should().Be("9ZZRPT00000000101");
        conDocs.GetProperty("veredicto").GetString().Should().Be("VIABLE");
        conDocs.GetProperty("modalidad").GetString().Should().Be("MASIVO");
        conDocs.GetProperty("tipo_analisis").GetString().Should().Be("SIMPLE");
        conDocs.GetProperty("diagnostico_nombre").GetString().Should().Be("Lote con documentos");
        conDocs.GetProperty("organismo").GetString().Should().Be("STRIA TTO PRUEBA");
        conDocs.GetProperty("fecha_diagnostico").GetString().Should().EndWith("-05:00", "instantes en hora Colombia");
        var ilegible = filas["RPT102"];
        ilegible.GetProperty("propietarios_documentos").GetString().Should().Be("CC No disponible");
        ilegible.GetProperty("locatario_documento").ValueKind.Should().Be(JsonValueKind.Null);
        ilegible.GetProperty("veredicto").ValueKind.Should().Be(JsonValueKind.Null);
        ilegible.GetProperty("modalidad").GetString().Should().Be("EXPRESS");
        ReportesDePrueba.Filas(pagina).Select(f => f.GetProperty("fecha_diagnostico").GetDateTimeOffset()).Should().BeInDescendingOrder();
    }

    // Registro de cada página: 2 filas PANTALLA/COMPANIA con las filas entregadas; la placa del filtro queda como bandera; acceso_id = id.
    [Fact]
    public async Task BE2_cada_pagina_deja_su_registro_con_placa_filtrada_y_acceso_id()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa, [new("RPT111"), new("RPT112"), new("RPT113")], ct);
        using var cliente = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));

        var primera = await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", "pagina=1&tamano=2"), ct);
        var segunda = await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", "pagina=2&tamano=2&placa=rpt11"), ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Content.ReadAsStringAsync(ct));
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, await segunda.Content.ReadAsStringAsync(ct));
        var p1 = await ReportesDePrueba.JsonAsync(primera, ct);
        var p2 = await ReportesDePrueba.JsonAsync(segunda, ct);
        ReportesDePrueba.Filas(p1).Should().HaveCount(2);
        ReportesDePrueba.Filas(p2).Should().ContainSingle();
        p2.GetProperty("total").GetInt32().Should().Be(3, "la placa se normaliza y se busca por prefijo");
        var accesos = await ReportesDePrueba.AccesosAsync(empresa, ct);
        accesos.Should().HaveCount(2);
        accesos.Should().OnlyContain(a => a.Formato == FormatoReporte.Pantalla && a.Alcance == AlcanceReporte.Compania && a.TipoReporte == TipoReporte.Vehiculos);
        accesos.Select(a => a.Id).Should().BeEquivalentTo([p1.GetProperty("acceso_id").GetGuid(), p2.GetProperty("acceso_id").GetGuid()]);
        accesos.Single(a => a.Id == p1.GetProperty("acceso_id").GetGuid()).Filas.Should().Be(2);
        var conPlaca = accesos.Single(a => a.Id == p2.GetProperty("acceso_id").GetGuid());
        conPlaca.Filas.Should().Be(1);
        conPlaca.Empresas.Should().Be(1);
        conPlaca.Filtros.Should().NotContain("RPT11", "SP-8: nunca el valor de la placa");
        JsonElement.Parse(conPlaca.Filtros).GetProperty("placa_filtrada").GetBoolean().Should().BeTrue();
        conPlaca.Desde.Should().Be(ReportesDePrueba.Hoy.AddDays(-30));
        conPlaca.Hasta.Should().Be(ReportesDePrueba.Hoy);
    }

    // Fail-closed: si el insert en accesos_reporte falla, la respuesta no trae datos.
    [Fact]
    public async Task BE2_sin_registro_no_hay_datos()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa, [new("RPT121", Propietarios: [new("CC", "1234567890")])], ct);
        await RomperRegistroAsync(api, ct);

        var respuesta = await api.Cliente(ReportesDePrueba.Token(api, empresa.Id)).GetAsync(ReportesDePrueba.Ruta("vehiculos"), ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        ((int)respuesta.StatusCode).Should().BeGreaterThanOrEqualTo(500);
        cuerpo.Should().NotContain("RPT121").And.NotContain("1234567890");
        ReportesDePrueba.Protector(api).Llamadas.Should().Be(0, "no se descifra nada antes de registrar el acceso");
    }

    // Consultas sin rastro del soporte: sin columna origen y sin contar los intentos MANUAL; duración sin el manual.
    [Fact]
    public async Task BE2_consultas_de_la_compania_sin_origen_ni_intentos_manuales()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await ReportesDePrueba.LoteAsync(empresa, [new("RPT131")], ct, manual: true);
        var celda = lote.Celdas.Single();
        await ReportesDePrueba.IntentoAsync(empresa, celda, DisparadorIntento.Auto, 1200, ct);
        await ReportesDePrueba.IntentoAsync(empresa, celda, DisparadorIntento.Manual, 50_000, ct);

        var respuesta = await api.Cliente(ReportesDePrueba.Token(api, empresa.Id)).GetAsync(ReportesDePrueba.Ruta("consultas"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var pagina = await ReportesDePrueba.JsonAsync(respuesta, ct);
        ReportesDePrueba.Claves(pagina).Should().NotContain("origen");
        var fila = ReportesDePrueba.Filas(pagina).Should().ContainSingle().Subject;
        fila.TryGetProperty("origen", out _).Should().BeFalse();
        fila.GetProperty("fuente").GetString().Should().Be("RUNT");
        fila.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        fila.GetProperty("intentos").GetInt32().Should().Be(1, "los intentos MANUAL del soporte no cuentan");
        fila.GetProperty("duracion_ms").GetInt32().Should().Be(1200);
        fila.GetProperty("reutilizada").GetString().Should().Be("NO");
        fila.GetProperty("placa").GetString().Should().Be("RPT131");
    }

    // Saneamientos y derivaciones: una fila por saneamiento y por ítem de derivación, con sus columnas.
    [Fact]
    public async Task BE2_saneamientos_y_derivaciones_traen_sus_columnas()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await ReportesDePrueba.LoteAsync(empresa, [new("RPT141"), new("RPT142")], ct, nombre: "Lote saneado");
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[0].Id, EstadoSaneamiento.Saneado, ct);
        await ReportesDePrueba.DerivacionAsync(empresa, lote.Id, lote.Vehiculos[1].Id, EstadoItemDerivacion.Creado, ct);
        using var cliente = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));

        var saneamientos = await cliente.GetAsync(ReportesDePrueba.Ruta("saneamientos", "estado=SANEADO"), ct);
        var derivaciones = await cliente.GetAsync(ReportesDePrueba.Ruta("derivaciones"), ct);

        saneamientos.StatusCode.Should().Be(HttpStatusCode.OK, await saneamientos.Content.ReadAsStringAsync(ct));
        var s = ReportesDePrueba.Filas(await ReportesDePrueba.JsonAsync(saneamientos, ct)).Should().ContainSingle().Subject;
        s.GetProperty("placa").GetString().Should().Be("RPT141");
        s.GetProperty("estado").GetString().Should().Be("SANEADO");
        s.GetProperty("diagnostico_nombre").GetString().Should().Be("Lote saneado");
        s.GetProperty("dias_hasta_cierre").GetInt32().Should().Be(0);
        s.GetProperty("renglones_total").GetInt32().Should().Be(0);
        derivaciones.StatusCode.Should().Be(HttpStatusCode.OK, await derivaciones.Content.ReadAsStringAsync(ct));
        var d = ReportesDePrueba.Filas(await ReportesDePrueba.JsonAsync(derivaciones, ct)).Should().ContainSingle().Subject;
        d.GetProperty("placa").GetString().Should().Be("RPT142");
        d.GetProperty("estado").GetString().Should().Be("CREADO");
        d.GetProperty("tipo_operacion").GetString().Should().Be("INDIVIDUAL");
        d.GetProperty("tipo_tramite").GetString().Should().Be("TRASPASO", "el nombre del catálogo");
        d.GetProperty("tramite_numero").GetString().Should().Be("TR-000123");
    }

    /// <summary>Trigger que hace fallar todo insert en accesos_reporte (mensaje sin datos).</summary>
    internal static async Task RomperRegistroAsync(Api.ApiDePrueba api, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            """
            CREATE FUNCTION diagnostico.prueba_romper_accesos() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'falla forzada de prueba'; END $$;
            CREATE TRIGGER prueba_romper_accesos BEFORE INSERT ON diagnostico.accesos_reporte FOR EACH ROW EXECUTE FUNCTION diagnostico.prueba_romper_accesos();
            """, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }
}
