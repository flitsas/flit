using System.Net;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-3 (R2; SP-8, SP-13, SP-15; contrato-api §5) — Excel del reporte: hojas «Aviso» (la primera), «Reporte» (aviso y
/// registro en la fila 1, encabezados en la 2) y «Filtros» (sin el valor de la placa); el documento como texto tal cual y todo texto que
/// parece fórmula como texto en línea con quotePrefix, sin ningún elemento <c>f</c>; una fila XLSX en <c>accesos_reporte</c>.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ExcelReportesTests
/// </code>
/// </example>
public sealed class ExcelReportesTests
{
    private const string Formula = "=HYPERLINK(\"http://x\",\"y\")";

    [Fact]
    public async Task BE3_excel_con_aviso_registro_texto_literal_y_sin_formulas()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await ReportesDePrueba.LoteAsync(empresa,
            [new("RPT601", Propietarios: [new("CC", "0012345")]), new("RPT602", Propietarios: [new("CC", "-1020")])], ct, nombre: Formula);
        await using (var db = empresa.Db())
        {
            await db.Set<VehiculoDiagnostico>().Where(v => v.Id == lote.Vehiculos[0].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.EstadoConsultas, "@SUM(1)"), ct);
        }

        var respuesta = await api.Cliente(ReportesDePrueba.Token(api, empresa.Id, descargar: true))
            .GetAsync(ReportesDePrueba.Ruta("vehiculos", "placa=RPT6", "/excel"), ct);

        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be(ExcelDePrueba.ContentType);
        var acceso = (await ReportesDePrueba.AccesosAsync(empresa, ct)).Should().ContainSingle().Subject;
        acceso.Formato.Should().Be(FormatoReporte.Xlsx);
        acceso.Filas.Should().Be(2);
        acceso.Empresas.Should().Be(1);
        acceso.Filtros.Should().NotContain("RPT6");

        var libro = ExcelDePrueba.Leer(bytes);
        libro.Hojas.Keys.Should().Equal("Aviso", "Reporte", "Filtros");
        var aviso = string.Join('\n', libro.Hojas["Aviso"].SelectMany(f => f));
        aviso.Should().Contain("Ley 1581").And.Contain($"Registro {acceso.Id}");
        var reporte = libro.Hojas["Reporte"];
        string.Join(' ', reporte[0]).Should().Contain("Ley 1581").And.Contain($"Registro {acceso.Id}");
        reporte[1].Should().Contain(["Fecha del diagnóstico", "Placa", "Propietarios (documento)"]);
        var documentos = Array.IndexOf(reporte[1], "Propietarios (documento)");
        var estado = Array.IndexOf(reporte[1], "Estado de las consultas");
        var nombre = Array.IndexOf(reporte[1], "Diagnóstico");
        var placa = Array.IndexOf(reporte[1], "Placa");
        var f601 = reporte.FindIndex(2, r => r.Length > placa && r[placa] == "RPT601");
        var f602 = reporte.FindIndex(2, r => r.Length > placa && r[placa] == "RPT602");
        reporte[f601][documentos].Should().Be("CC 0012345", "el documento va como texto, con sus ceros");
        reporte[f602][documentos].Should().Be("CC -1020");
        reporte[f601][estado].Should().Be("@SUM(1)");
        reporte[f601][nombre].Should().Be(Formula);
        libro.EsLiteral("Reporte", f601, estado).Should().BeTrue("SP-13: @ al inicio va como texto literal");
        libro.EsLiteral("Reporte", f601, nombre).Should().BeTrue("SP-13: = al inicio va como texto literal");
        libro.Formulas.Should().Be(0, "ningún elemento f en todo el libro");
        var filtros = string.Join('\n', libro.Hojas["Filtros"].SelectMany(f => f));
        filtros.Should().Contain("vehiculos").And.Contain(ReportesDePrueba.Hoy.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        filtros.Should().NotContain("RPT6", "la hoja Filtros no lleva el valor de la placa");
    }
}
