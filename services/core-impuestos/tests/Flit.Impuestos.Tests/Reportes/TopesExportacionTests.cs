using System.Net;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Liquidaciones;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Reportes;

/// <summary>
/// HU #14694 (M14; F4b-CF1, F4b-CF2 con DA-9b) — topes de filas de la exportación: Excel hasta 5000 y PDF hasta 1000. Con una fila
/// más responde 422 <c>TOPE_EXPORTACION</c> con <c>tope</c> y el literal con <c>{tope}</c> sustituido, sin generar un archivo
/// truncado. En el tope exacto sale el archivo completo. No hay límite de exportaciones por usuario: la misma descarga se repite.
/// </summary>
/// <example>
/// <code>
/// var r = await cliente.GetAsync("/api/v1/impuestos/reportes/detalle/excel", ct); // 422 si pasa de 5000 filas
/// </code>
/// </example>
public sealed class TopesExportacionTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task Excel_sale_completo_con_5000_filas_y_con_5001_responde_422_sin_truncar()
    {
        var ct = Preparar();
        var empresa = await EmpresaConPlacasAsync(5000, ct);
        var cliente = Api.Cliente(Api.Token(empresa: empresa, permisos: E.Exportar));

        using var completo = await cliente.GetAsync($"{E.Reportes}/detalle/excel", ct);
        completo.StatusCode.Should().Be(HttpStatusCode.OK, await completo.Content.ReadAsStringAsync(ct));
        EntornoMasiva.LeerXlsx(await completo.Content.ReadAsByteArrayAsync(ct)).Filas.Should().HaveCount(5001, "encabezado + 5000 filas");

        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, null, new ItemSemilla("TOP999"));
        using var excedido = await cliente.GetAsync($"{E.Reportes}/detalle/excel", ct);
        var cuerpo = await LiquidacionIndividualEndpointsTests.EsperarError(excedido, HttpStatusCode.UnprocessableEntity, "TOPE_EXPORTACION",
            "El reporte supera el máximo de 5000 filas para exportar: ajusta los filtros.");
        cuerpo.GetProperty("tope").GetInt32().Should().Be(5000);
        excedido.Content.Headers.ContentType!.MediaType.Should().Be("application/json", "nunca un archivo truncado");
    }

    [Fact]
    public async Task Pdf_sale_completo_con_1000_filas_y_con_1001_responde_422_y_se_repite_sin_limite()
    {
        var ct = Preparar();
        var empresa = await EmpresaConPlacasAsync(1000, ct);
        var cliente = Api.Cliente(Api.Token(empresa: empresa, permisos: E.Exportar));

        for (var i = 0; i < 3; i++)
        {
            using var r = await cliente.GetAsync($"{E.Reportes}/detalle/pdf", ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK, "sin límite de exportaciones por usuario (DA-9b)");
            r.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        }

        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, null, new ItemSemilla("TOP998"));
        var cuerpo = await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Reportes}/detalle/pdf", ct),
            HttpStatusCode.UnprocessableEntity, "TOPE_EXPORTACION", "El reporte supera el máximo de 1000 filas para exportar: ajusta los filtros.");
        cuerpo.GetProperty("tope").GetInt32().Should().Be(1000);
        using var excel = await cliente.GetAsync($"{E.Reportes}/detalle/excel", ct);
        excel.StatusCode.Should().Be(HttpStatusCode.OK, "1001 filas caben en el tope del Excel");
    }

    private async Task<Guid> EmpresaConPlacasAsync(int placas, CancellationToken ct)
    {
        var empresa = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            [.. Enumerable.Range(0, placas).Select(i => new ItemSemilla($"T{i:D5}", ResultadoStatus: "con_deuda", Total: i))]);
        return empresa;
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        return TestContext.Current.CancellationToken;
    }
}
