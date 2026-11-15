using System.Reflection;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Estadisticas;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, corrección CR-BE-9 y CR-BE-10 (ADR-0088 §«Notas para agentes») — prueba de arquitectura por grep auditable y reflexión:
/// <list type="bullet">
/// <item><c>Modulos/Reportes/Catalogo/</c> y <c>Modulos/Estadisticas/</c> no dependen de <c>Modulos/Backoffice/</c>: el backoffice usa
/// el motor y la calculadora, nunca al revés (sin ciclos Reportes ↔ Backoffice ni Estadisticas ↔ Backoffice).</item>
/// <item>El reporte <c>consumo</c>, que solo existe en el backoffice, vive en <c>Modulos/Backoffice/</c>.</item>
/// <item>El módulo de estadísticas no expone un <c>AddEstadisticas</c> vacío.</item>
/// <item>El rango de días (366 con los dos extremos, día de Colombia) se define una sola vez, en <c>Modulos/Comun/RangoDias</c>.</item>
/// </list>
/// Los comentarios no cuentan; <c>bin/</c> y <c>obj/</c> quedan fuera.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DependenciasEstadisticasReportesTests
/// </code>
/// </example>
public sealed partial class DependenciasEstadisticasReportesTests
{
    private static readonly string[] SinBackoffice = ["Reportes/Catalogo", "Estadisticas"];

    private static string Modulos => Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src", "Flit.Diagnostico.Api", "Modulos");

    // CR-BE-9 — ni el catálogo de reportes ni las estadísticas nombran tipos del backoffice.
    [Fact]
    public void Catalogo_de_reportes_y_estadisticas_no_dependen_del_backoffice()
    {
        var usos = SinBackoffice
            .SelectMany(m => IgnoreQueryFiltersSoloEnBackofficeTests.Usos(Path.Combine(Modulos, m), ReferenciaAlBackoffice()).Select(u => $"{m}/{u.Archivo}:{u.Linea}"))
            .ToList();

        usos.Should().BeEmpty("el backoffice depende del motor y de la calculadora, no al revés");
    }

    // CR-BE-9 — el escáner detecta un using o un tipo calificado del backoffice y no confunde AlcanceReporte.BackofficeGlobal.
    [Fact]
    public void El_escaner_detecta_una_referencia_al_backoffice()
    {
        var raiz = Directory.CreateTempSubdirectory("dgn-dep-").FullName;
        try
        {
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "A.cs", "using Flit.Diagnostico.Api.Modulos.Backoffice;");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "B.cs", "var x = 1;\nBackoffice.ReportesBackofficeEndpoints.Map(api);");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "C.cs", "var a = AlcanceReporte.BackofficeGlobal;\n/// <see cref=\"Backoffice.X\"/>");

            var usos = IgnoreQueryFiltersSoloEnBackofficeTests.Usos(raiz, ReferenciaAlBackoffice());

            usos.Select(u => $"{u.Archivo}:{u.Linea}").Should().BeEquivalentTo(["A.cs:1", "B.cs:2"]);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    // CR-BE-9 — consumo (solo backoffice) vive en Modulos/Backoffice y AddEstadisticas, que no registraba nada, ya no existe.
    [Fact]
    public void Consumo_vive_en_el_backoffice_y_no_hay_AddEstadisticas_vacio()
    {
        var api = typeof(Program).Assembly;

        api.GetType("Flit.Diagnostico.Api.Modulos.Backoffice.ReporteConsumo").Should().NotBeNull();
        api.GetType("Flit.Diagnostico.Api.Modulos.Reportes.Catalogo.ReporteConsumo").Should().BeNull();
        File.Exists(Path.Combine(Modulos, "Backoffice", "ReporteConsumo.cs")).Should().BeTrue();
        typeof(EstadisticasRegistro).GetMethod("AddEstadisticas", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Should().BeNull();
    }

    // CR-BE-10 — el tope de 366 días y el parseo del rango viven en una sola pieza compartida.
    [Fact]
    public void El_rango_de_dias_se_define_una_sola_vez_en_Comun()
    {
        typeof(Program).Assembly.GetType("Flit.Diagnostico.Api.Modulos.Comun.RangoDias").Should().NotBeNull();

        var definiciones = IgnoreQueryFiltersSoloEnBackofficeTests.Usos(Modulos, DefinicionDe366());

        definiciones.Select(u => u.Archivo).Should().Equal("Comun/RangoDias.cs");
    }

    [GeneratedRegex(@"\bModulos\.Backoffice\b|\bBackoffice\.[A-Z]")]
    private static partial Regex ReferenciaAlBackoffice();

    [GeneratedRegex(@"=\s*366\s*;")]
    private static partial Regex DefinicionDe366();
}
