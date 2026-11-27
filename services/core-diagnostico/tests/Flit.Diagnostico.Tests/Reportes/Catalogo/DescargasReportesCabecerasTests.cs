using System.Net;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-3 (SP-1, SP-9, SP-10, SP-14; contrato-api §5) — R2/R3 exigen <c>reportes.ver</c> y <c>reportes.descargar</c>; cada
/// descarga lleva <c>nosniff</c>, CSP sandbox, <c>no-store</c> y <c>Pragma: no-cache</c>, el Content-Type exacto y un nombre de archivo
/// fijo sin PII; los logs de las exportaciones no traen el documento, la placa ni el VIN.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DescargasReportesCabecerasTests
/// </code>
/// </example>
public sealed partial class DescargasReportesCabecerasTests
{
    [GeneratedRegex(@"^diagnostico-reporte-[a-z]+-\d{8}-\d{8}(-global)?\.(xlsx|pdf)$")]
    private static partial Regex NombreArchivo();

    [Fact]
    public async Task BE3_permisos_cabeceras_y_nombre_de_archivo()
    {
        var logs = new CapturaLogs();
        await using var api = await ReportesDePrueba.ArrancarAsync(logs: logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa, [new("RPT901", Vin: "9ZZDES00000000901", Propietarios: [new("CC", "8080808080")])], ct);
        using var sinDescargar = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));
        using var sinVer = api.Cliente(ReportesDePrueba.Token(api, empresa.Id, ver: false, descargar: true));
        using var conTodo = api.Cliente(ReportesDePrueba.Token(api, empresa.Id, descargar: true));
        var desde = ReportesDePrueba.Hoy.AddDays(-30).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var hasta = ReportesDePrueba.Hoy.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var tipo in new[] { "vehiculos", "consultas", "saneamientos", "derivaciones" })
        {
            foreach (var (formato, tipoContenido) in new[] { ("excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"), ("pdf", "application/pdf") })
            {
                var ruta = ReportesDePrueba.Ruta(tipo, sufijo: "/" + formato);
                (await sinDescargar.GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{ruta} sin reportes.descargar");
                (await sinVer.GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{ruta} sin reportes.ver");

                var respuesta = await conTodo.GetAsync(ruta, ct);
                respuesta.StatusCode.Should().Be(HttpStatusCode.OK, ruta);
                respuesta.Content.Headers.ContentType!.ToString().Should().Be(tipoContenido);
                respuesta.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
                respuesta.Headers.GetValues("Content-Security-Policy").Should().Equal(DescargaSeguraFiltro.PoliticaContenido);
                respuesta.Headers.CacheControl!.NoStore.Should().BeTrue();
                respuesta.Headers.Pragma.ToString().Should().Be("no-cache");
                var disposicion = respuesta.Content.Headers.ContentDisposition!;
                disposicion.DispositionType.Should().Be("attachment");
                var nombre = disposicion.FileName!.Trim('"');
                NombreArchivo().IsMatch(nombre).Should().BeTrue(nombre);
                nombre.Should().Be($"diagnostico-reporte-{tipo}-{desde}-{hasta}.{(formato == "excel" ? "xlsx" : "pdf")}");
            }
        }

        (await conTodo.GetAsync(ReportesDePrueba.Ruta("consumo", sufijo: "/excel"), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var pii in new[] { "RPT901", "9ZZDES00000000901", "8080808080" })
            logs.Lineas.Should().NotContain(l => l.Contains(pii, StringComparison.Ordinal), $"SP-9: «{pii}» no va a los logs de las exportaciones");
        logs.Lineas.Should().Contain(l => l.Contains("XLSX", StringComparison.Ordinal) && l.Contains("entregado", StringComparison.Ordinal));
    }

    // HU #14752 SP-8 / SP-5 — el Excel del lote (con PII en claro, D3) sale con no-store, Pragma: no-cache, nosniff y como adjunto
    // diagnostico-{8}.xlsx (sin placa ni documento); sin diagnostico.reportes.descargar 403; con PROPIOS, el lote de otro usuario 404.
    [Fact]
    public async Task HU14752_excel_del_lote_cabeceras_permiso_y_visibilidad()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var creador = ExcelDePrueba.Token(api, empresa.Id);
        var otro = ExcelDePrueba.Token(api, empresa.Id);
        var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, creador: Tablero.Tablero.Usuario(creador), alcance: [Fuente.Runt],
            vehiculos: [new("RPT951", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        await Tablero.Tablero.PayloadAsync(empresa, lote.Celdas.Single(), """{"placa":"RPT951","clase":"AUTOMOVIL","propietarios":[],"locatarios":0}""", ct);

        var respuesta = await api.Cliente(creador).GetAsync(ExcelDePrueba.Ruta(lote.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await ExcelDePrueba.CuerpoSiNoEsExcel(respuesta, ct));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be(ExcelDePrueba.ContentType);
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue();
        respuesta.Headers.Pragma.ToString().Should().Be("no-cache");
        respuesta.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        var disposicion = respuesta.Content.Headers.ContentDisposition!;
        disposicion.DispositionType.Should().Be("attachment");
        disposicion.FileName!.Trim('"').Should().Be($"diagnostico-{lote.Id.ToString()[..8]}.xlsx").And.NotContain("RPT951");

        var soloVer = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        (await api.Cliente(soloVer).GetAsync(ExcelDePrueba.Ruta(lote.Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var db = empresa.Db())
        {
            db.Add(new Flit.Diagnostico.Api.Modulos.Configuracion.ConfiguracionEmpresa { VisibilidadUsuario = Flit.Diagnostico.Domain.Configuracion.VisibilidadUsuario.Propios });
            await db.SaveChangesAsync(ct);
        }

        var ajeno = await api.Cliente(otro).GetAsync(ExcelDePrueba.Ruta(lote.Id), ct);
        ajeno.StatusCode.Should().Be(HttpStatusCode.NotFound, "PROPIOS: el lote es de otro usuario");
        (await ajeno.Content.ReadAsStringAsync(ct)).Should().NotContain("RPT951");
    }
}
