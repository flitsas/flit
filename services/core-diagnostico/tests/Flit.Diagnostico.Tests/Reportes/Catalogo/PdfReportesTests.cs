using System.Diagnostics;
using System.Net;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;
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

    // CR-BE-5 — con el token ya cancelado no se genera nada; cancelado a mitad, la generación de 1 000 filas se corta en una página y
    // libera el hilo mucho antes de terminar. Mide además el PDF completo de 1 000 filas (el tope por defecto).
    [Fact]
    public void BE3_pdf_respeta_la_cancelacion_por_bloques()
    {
        var archivo = ArchivoDe(1000);
        using (var cancelado = new CancellationTokenSource())
        {
            cancelado.Cancel();
            FluentActions.Invoking(() => PdfReporte.Generar(archivo, cancelado.Token)).Should().Throw<OperationCanceledException>();
        }

        var reloj = Stopwatch.StartNew();
        var completo = PdfReporte.Generar(archivo, TestContext.Current.CancellationToken);
        var total = reloj.ElapsedMilliseconds;
        Encoding.ASCII.GetString(completo, 0, 5).Should().Be("%PDF-");
        TestContext.Current.TestOutputHelper?.WriteLine($"MEDICION generar PDF de 1000 filas: {total} ms, {completo.Length} bytes");

        var corte = Math.Max(100, total / 5);
        using var aMitad = new CancellationTokenSource(TimeSpan.FromMilliseconds(corte));
        reloj.Restart();
        FluentActions.Invoking(() => PdfReporte.Generar(archivo, aMitad.Token)).Should().Throw<OperationCanceledException>();
        var cortado = reloj.ElapsedMilliseconds;
        TestContext.Current.TestOutputHelper?.WriteLine($"MEDICION PDF de 1000 filas cancelado a los {corte} ms: cortó a los {cortado} ms");
        cortado.Should().BeLessThan(total * 3 / 4, "la vigía corta en la página siguiente, no al final");
    }

    /// <summary>Archivo de <paramref name="filas"/> vehículos ficticios (RPT#####) con las columnas de la compañía.</summary>
    private static ArchivoReporte ArchivoDe(int filas)
    {
        var columnas = DefinicionReporte.Columnas(TipoReporte.Vehiculos, AlcanceReporte.Compania);
        var filtro = new FiltroReporte(TipoReporte.Vehiculos, new DateOnly(2026, 10, 17), new DateOnly(2026, 11, 15), new Dictionary<string, IReadOnlyList<string>>(), null, 1, 25);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> datos =
        [
            .. Enumerable.Range(0, filas).Select(i => (IReadOnlyDictionary<string, object?>)columnas.ToDictionary(
                c => c.Clave, c => (object?)(c.Clave == "placa" ? $"RPT{i:D5}" : $"VALOR {i % 7}"), StringComparer.Ordinal)),
        ];
        return new ArchivoReporte(filtro, AlcanceReporte.Compania, columnas, datos, Guid.CreateVersion7(), new DateTimeOffset(2026, 11, 15, 17, 0, 0, TimeSpan.Zero));
    }

    /// <summary>¿Aparece el texto en el PDF crudo, literal o como cadena UTF-16BE hexadecimal (metadatos con tildes)?</summary>
    private static bool Tiene(string crudo, string texto)
    {
        var hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(texto));
        return crudo.Contains(texto, StringComparison.Ordinal) || crudo.Contains(hex, StringComparison.OrdinalIgnoreCase)
            || crudo.Contains(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(texto)), StringComparison.Ordinal);
    }
}
