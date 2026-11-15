using System.Net;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-3 (R3; SP-8, SP-15) — PDF del reporte: el aviso Ley 1581 y «Registro {acceso_id}» en el pie de cada página, metadatos
/// Author «FLIT» y Creator «FLIT Diagnóstico» sin usuario, compañía ni placa; una fila PDF en <c>accesos_reporte</c>.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PdfReportesTests
/// </code>
/// </example>
public sealed class PdfReportesTests
{
    [Fact]
    public async Task BE3_pdf_con_aviso_y_registro_en_cada_pagina_y_metadatos_sin_PII()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa, [.. Enumerable.Range(0, 60).Select(i => new VehiculoReporteSembrado($"RPT7{i:D2}"))], ct);

        var respuesta = await api.Cliente(ReportesDePrueba.Token(api, empresa.Id, descargar: true))
            .GetAsync(ReportesDePrueba.Ruta("consultas", sufijo: "/pdf"), ct);

        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, Encoding.UTF8.GetString(bytes));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var acceso = (await ReportesDePrueba.AccesosAsync(empresa, ct)).Should().ContainSingle().Subject;
        acceso.Formato.Should().Be(FormatoReporte.Pdf);
        acceso.Filas.Should().Be(60);
        var paginas = PdfTextExtractor.ExtractPages(bytes);
        paginas.Should().HaveCountGreaterThan(1, "60 filas no caben en una página");
        foreach (var pagina in paginas)
        {
            PdfTextExtractor.Contains(pagina, "Ley 1581").Should().BeTrue("SP-15: el aviso va en cada página");
            PdfTextExtractor.Contains(pagina, $"Registro {acceso.Id}").Should().BeTrue("SP-15: el registro va en cada página");
        }

        PdfTextExtractor.Contains(PdfTextExtractor.Extract(bytes), "RPT759").Should().BeTrue("el PDF trae las filas");
        var crudo = Encoding.Latin1.GetString(bytes);
        Tiene(crudo, "FLIT Diagnóstico").Should().BeTrue("Creator");
        foreach (var prohibido in new[] { "operador@empresa.test", "Empresa de prueba", "RPT700" })
            Tiene(crudo, prohibido).Should().BeFalse($"los metadatos no llevan «{prohibido}»");
    }

    /// <summary>¿Aparece el texto en el PDF crudo, literal o como cadena UTF-16BE hexadecimal (metadatos con tildes)?</summary>
    private static bool Tiene(string crudo, string texto)
    {
        var hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(texto));
        return crudo.Contains(texto, StringComparison.Ordinal) || crudo.Contains(hex, StringComparison.OrdinalIgnoreCase)
            || crudo.Contains(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(texto)), StringComparison.Ordinal);
    }
}
