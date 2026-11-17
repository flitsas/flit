using System.Net;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-2 (SP-9, SP-11; contrato-api §0, §2 y §3) — lectura de la consulta del reporte: tipo cerrado (404
/// <c>REPORTE_NO_ENCONTRADO</c>, también <c>consumo</c> en la compañía), <c>documento</c>/<c>nombre</c>/<c>empresa</c> prohibidos, rango
/// obligatorio de hasta 366 días con los dos extremos incluidos y sin pasar de hoy, paginación acotada, enums del tipo y filtros que sí
/// acotan las filas. Todo con una sola API. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~FiltroReporteTests
/// </code>
/// </example>
public sealed class FiltroReporteTests
{
    [Fact]
    public async Task BE2_filtros_invalidos_responden_con_su_codigo()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        using var cliente = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));
        var hoy = ReportesDePrueba.Hoy;
        string R(string tipo, string consulta) => $"{ReportesDePrueba.Base}/{tipo}?{consulta}";
        string D(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        (string Ruta, HttpStatusCode Estado, string? Codigo, string? Campo)[] casos =
        [
            (ReportesDePrueba.Ruta("desconocido"), HttpStatusCode.NotFound, "REPORTE_NO_ENCONTRADO", null),
            (ReportesDePrueba.Ruta("consumo"), HttpStatusCode.NotFound, "REPORTE_NO_ENCONTRADO", null),
            (ReportesDePrueba.Ruta("vehiculos", "documento=1"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "documento"),
            (ReportesDePrueba.Ruta("vehiculos", "nombre=x"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "nombre"),
            (ReportesDePrueba.Ruta("consultas", $"empresa={Guid.NewGuid()}"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "empresa"),
            (ReportesDePrueba.Ruta("vehiculos", "tamano=101"), HttpStatusCode.BadRequest, "PAGINACION_INVALIDA", "tamano"),
            (ReportesDePrueba.Ruta("vehiculos", "tamano=0"), HttpStatusCode.BadRequest, "PAGINACION_INVALIDA", "tamano"),
            (ReportesDePrueba.Ruta("vehiculos", "pagina=0"), HttpStatusCode.BadRequest, "PAGINACION_INVALIDA", "pagina"),
            (ReportesDePrueba.Ruta("vehiculos", "pagina=401&tamano=25"), HttpStatusCode.BadRequest, "PAGINACION_INVALIDA", "pagina"),
            (R("vehiculos", $"hasta={D(hoy)}"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "desde"),
            (R("vehiculos", $"desde={D(hoy)}"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "hasta"),
            (R("vehiculos", $"desde=2026-13-01&hasta={D(hoy)}"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "desde"),
            (R("vehiculos", $"desde={D(hoy.AddDays(-366))}&hasta={D(hoy)}"), HttpStatusCode.BadRequest, "RANGO_INVALIDO", null),
            (R("vehiculos", $"desde={D(hoy.AddDays(-1))}&hasta={D(hoy.AddDays(1))}"), HttpStatusCode.BadRequest, "RANGO_INVALIDO", null),
            (R("vehiculos", $"desde={D(hoy)}&hasta={D(hoy.AddDays(-2))}"), HttpStatusCode.BadRequest, "RANGO_INVALIDO", null),
            (ReportesDePrueba.Ruta("consultas", "estado=XYZ"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "estado"),
            (ReportesDePrueba.Ruta("consultas", "estado=SOLICITADO"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "estado"),
            (ReportesDePrueba.Ruta("consultas", "fuente=RUES"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "fuente"),
            (ReportesDePrueba.Ruta("vehiculos", "modalidad=EXPRESS&modalidad=MASIVO"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "modalidad"),
            (ReportesDePrueba.Ruta("vehiculos", "veredicto=QUIZA"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "veredicto"),
            (ReportesDePrueba.Ruta("vehiculos", "placa=%25"), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "placa"),
            (ReportesDePrueba.Ruta("vehiculos", "placa=" + new string('A', 21)), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "placa"),
            (R("vehiculos", $"desde={D(hoy.AddDays(-365))}&hasta={D(hoy)}"), HttpStatusCode.OK, null, null),
            (ReportesDePrueba.Ruta("vehiculos", "desconocido=1"), HttpStatusCode.OK, null, null),
            (ReportesDePrueba.Ruta("consultas", "estado=ERROR&estado=NA&fuente=RUNT&fuente=SIMIT_VEHICULO"), HttpStatusCode.OK, null, null),
        ];

        foreach (var (ruta, estado, codigo, campo) in casos)
        {
            var respuesta = await cliente.GetAsync(ruta, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(estado, $"{ruta}: {cuerpo}");
            if (codigo is not null)
                (await ReportesDePrueba.CodigoAsync(respuesta, ct)).Should().Be(codigo, ruta);
            if (campo is not null)
                (await ReportesDePrueba.JsonAsync(respuesta, ct)).GetProperty("campo").GetString().Should().Be(campo, ruta);
        }

        (await ReportesDePrueba.AccesosAsync(empresa, ct)).Should().HaveCount(casos.Count(c => c.Estado == HttpStatusCode.OK),
            "una petición rechazada no deja registro");
    }

    [Fact]
    public async Task BE2_filtros_validos_acotan_las_filas()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa, [new("RPT501", Veredicto: "VIABLE"), new("RPT502", Veredicto: "BLOQUEADO"), new("RPT503")], ct);
        await ReportesDePrueba.LoteAsync(empresa, [new("XYZ504")], ct, express: true, modo: Flit.Diagnostico.Domain.Diagnosticos.ModoDiagnostico.ConAnalisis,
            tipoTramite: "TRASPASO_STANDARD");
        using var cliente = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));

        async Task<List<string>> PlacasAsync(string tipo, string consulta)
        {
            var respuesta = await cliente.GetAsync(ReportesDePrueba.Ruta(tipo, consulta), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
            return [.. ReportesDePrueba.Filas(await ReportesDePrueba.JsonAsync(respuesta, ct)).Select(f => f.GetProperty("placa").GetString()!)];
        }

        (await PlacasAsync("vehiculos", "modalidad=EXPRESS")).Should().Equal("XYZ504");
        (await PlacasAsync("vehiculos", "tipo_analisis=SIMPLE")).Should().BeEquivalentTo("RPT501", "RPT502", "RPT503");
        (await PlacasAsync("vehiculos", "veredicto=BLOQUEADO")).Should().Equal("RPT502");
        (await PlacasAsync("vehiculos", "veredicto=SIN_DICTAMEN")).Should().BeEquivalentTo("RPT503", "XYZ504");
        (await PlacasAsync("vehiculos", "placa=rpt-50")).Should().BeEquivalentTo("RPT501", "RPT502", "RPT503");
        (await PlacasAsync("consultas", "fuente=SIMIT_VEHICULO")).Should().BeEmpty();
        (await PlacasAsync("consultas", "estado=COMPLETADO&fuente=RUNT")).Should().HaveCount(4);
        var conAnalisis = ReportesDePrueba.Filas(await ReportesDePrueba.JsonAsync(await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", "placa=XYZ"), ct), ct)).Single();
        conAnalisis.GetProperty("tipo_tramite").GetString().Should().Be("TRASPASO", "el nombre del catálogo");
        conAnalisis.GetProperty("tipo_analisis").GetString().Should().Be("CON_ANALISIS");
    }
}
