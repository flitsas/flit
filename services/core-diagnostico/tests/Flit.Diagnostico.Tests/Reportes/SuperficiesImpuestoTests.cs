using System.Text.Json;
using Flit.Diagnostico.Api.Infraestructura.Excel;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using FluentAssertions;
using Xunit;
using Celda = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// C3-8: el impuesto en ERROR no imprime «$ 0» ni «sin deuda»; la deuda sin monto dice «Deuda sin valor informado»;
/// sin deuda no imprime fechas ni valores que no vinieron; el dato parcial se reconoce sin armar un monto.
/// </summary>
public sealed class SuperficiesImpuestoTests
{
    private static readonly DateTimeOffset GeneradoEn = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Deuda_sin_monto_omite_monto_total_y_no_proyecta_el_departamento()
    {
        using var doc = JsonDocument.Parse("""{"tiene_deuda":true,"vigencias":[],"departamento_codigo":"05","proveedor":"X"}""");
        var resultado = ResultadoImpuestoDetalle.Desde(doc.RootElement);

        resultado!.TieneDeuda.Should().BeTrue();
        resultado.MontoTotal.Should().BeNull();
        var json = JsonSerializer.Serialize(resultado);
        json.Should().NotContain("monto_total");
        json.Should().NotContain("departamento");
        json.Should().NotContain("proveedor");
        json.Should().Contain("\"tiene_deuda\":true");
    }

    [Fact]
    public void Sin_deuda_no_conserva_fechas_ni_valores_vacios()
    {
        using var doc = JsonDocument.Parse("""
            {"tiene_deuda":false,"vigencias":[],"ultimo_pago":{"anio":0,"valor":0,"fecha":" "},"fecha_consulta_proveedor":"","intereses_mora":-1}
            """);
        var resultado = ResultadoImpuestoDetalle.Desde(doc.RootElement);

        resultado!.TieneDeuda.Should().BeFalse();
        resultado.MontoTotal.Should().BeNull();
        resultado.UltimoPago.Should().BeNull();
        resultado.FechaConsulta.Should().BeNull();
        resultado.InteresesMora.Should().BeNull();
    }

    [Fact]
    public void Dato_parcial_se_reconoce_y_no_es_un_resultado()
    {
        using var doc = JsonDocument.Parse("""{"dato_parcial":true,"monto_total":120000}""");
        ResultadoImpuestoDetalle.EsDatoParcial(doc.RootElement).Should().BeTrue();
        ResultadoImpuestoDetalle.EsDatoParcial(null).Should().BeFalse();
    }

    [Fact]
    public void Error_de_impuesto_en_pdf_no_imprime_cero_ni_sin_deuda()
    {
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(Error()), GeneradoEn));
        var seccion = PdfDePrueba.Seccion(texto, "IMPUESTO VEHICULAR DEPARTAMENTAL");
        PdfDePrueba.Tiene(seccion, "No confirmado").Should().BeTrue(seccion);
        PdfDePrueba.Tiene(seccion, "$ 0").Should().BeFalse(seccion);
        seccion.Contains("sin deuda", StringComparison.OrdinalIgnoreCase).Should().BeFalse(seccion);
    }

    [Fact]
    public void Deuda_sin_monto_en_pdf_dice_deuda_sin_valor_informado()
    {
        var impuesto = new ResultadoImpuestoDetalle(null, [], true);
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(Completado(impuesto)), GeneradoEn));
        var seccion = PdfDePrueba.Seccion(texto, "IMPUESTO VEHICULAR DEPARTAMENTAL");
        PdfDePrueba.Tiene(seccion, PdfVehiculo.DeudaSinValor).Should().BeTrue(seccion);
        PdfDePrueba.Tiene(seccion, "$ 0").Should().BeFalse(seccion);
    }

    [Fact]
    public void Sin_deuda_en_pdf_no_imprime_fechas_que_no_vinieron()
    {
        var impuesto = new ResultadoImpuestoDetalle(null, [], false);
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(Completado(impuesto)), GeneradoEn));
        var seccion = PdfDePrueba.Seccion(texto, "IMPUESTO VEHICULAR DEPARTAMENTAL");
        PdfDePrueba.Tiene(seccion, PdfVehiculo.SinDeuda).Should().BeTrue(seccion);
        PdfDePrueba.Tiene(seccion, "Fecha del último pago").Should().BeFalse(seccion);
        PdfDePrueba.Tiene(seccion, "Avalúo comercial").Should().BeFalse(seccion);
        PdfDePrueba.Tiene(seccion, "$ 0").Should().BeFalse(seccion);
    }

    [Fact]
    public void Excel_no_escribe_cero_en_error_ni_en_monto_null()
    {
        var error = CeldaMonto(Error());
        error.Should().BeOfType<Celda.Vacia>();

        var sinValor = CeldaMonto(Completado(new ResultadoImpuestoDetalle(null, [], true)));
        sinValor.Should().BeOfType<Celda.Str>().Which.Value.Should().Be(PdfVehiculo.DeudaSinValor);

        var sinDeuda = CeldaMonto(Completado(new ResultadoImpuestoDetalle(null, [], false)));
        sinDeuda.Should().BeOfType<Celda.Str>().Which.Value.Should().Be(PdfVehiculo.SinDeuda);

        var estado = EstadoDe(Error());
        estado.Should().BeOfType<Celda.Str>().Which.Value.Should().Be("No confirmado");
    }

    private static Celda CeldaMonto(SeccionReporte seccion) => Fila(seccion)[Indice("Total adeudado", seccion)];

    private static Celda EstadoDe(SeccionReporte seccion) => Fila(seccion)[Indice("Estado", seccion)];

    private static int Indice(string sufijo, SeccionReporte seccion)
    {
        var hoja = Hoja(seccion);
        return hoja.Columns.ToList().FindIndex(c => c.Header.Contains(sufijo, StringComparison.Ordinal) && c.Header.StartsWith("Impuesto", StringComparison.Ordinal));
    }

    private static IReadOnlyList<Celda> Fila(SeccionReporte seccion) => Hoja(seccion).Rows[0];

    private static TabularWorkbookWriter.Sheet Hoja(SeccionReporte seccion)
    {
        var id = Guid.NewGuid();
        var lote = new ContenidoReporteLote(
            new EncabezadoLoteReporte(id, "Lote", "Empresa", "Ana", GeneradoEn, "Diagnóstico Simple"),
            [new VehiculoReporte(id, 1, "TST001", "PLACA", "TST001", null, [seccion])]);
        return ExcelLote.Hojas(lote, new Dictionary<Guid, DatosTecnicosVehiculo>(), null)[0];
    }

    private static ContenidoReporteVehiculo Contenido(SeccionReporte seccion) => new(
        new EncabezadoReporte(Guid.NewGuid(), "Lote", "Empresa", "Ana", GeneradoEn, "Diagnóstico Simple", "TST001", "PLACA", "TST001", null),
        [seccion]);

    private static SeccionReporte Error() =>
        new("IMPUESTO_DEPARTAMENTAL", "POR_VEHICULO", EstadoSeccion.NoDisponible, null, "consulta no disponible", null, null, null);

    private static SeccionReporte Completado(ResultadoImpuestoDetalle impuesto) =>
        new("IMPUESTO_DEPARTAMENTAL", "POR_VEHICULO", EstadoSeccion.Completado, null, null, GeneradoEn, impuesto, null);
}
