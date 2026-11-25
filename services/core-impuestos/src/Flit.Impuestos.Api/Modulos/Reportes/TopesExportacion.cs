using System.Globalization;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Reportes;

/// <summary>Formato de exportación de un reporte.</summary>
public enum FormatoExportacion
{
    Excel,
    Pdf,
}

/// <summary>
/// Topes de filas de la exportación (plan §5.1, F4b-CF1; impuestos.v1.yaml 1.1.0): Excel 5000 y PDF 1000. Se comprueba con el total
/// del reporte filtrado <b>antes</b> de leer las filas: con más, 422 <c>TOPE_EXPORTACION</c> con <c>tope</c> y el literal con
/// <c>{tope}</c> sustituido; nunca se trunca en silencio. No hay límite de exportaciones por usuario (DA-9b).
/// </summary>
/// <example>
/// <code>
/// TopesExportacion.Exigir(FormatoExportacion.Pdf, total); // 422 si total &gt; 1000
/// </code>
/// </example>
internal static class TopesExportacion
{
    public const int Excel = 5000;
    public const int Pdf = 1000;
    public const string Codigo = "TOPE_EXPORTACION";
    public const string Literal = "El reporte supera el máximo de {tope} filas para exportar: ajusta los filtros.";

    public static int De(FormatoExportacion formato) => formato == FormatoExportacion.Excel ? Excel : Pdf;

    public static void Exigir(FormatoExportacion formato, int filas)
    {
        var tope = De(formato);
        if (filas > tope)
            throw new ReglaException(Codigo, Literal.Replace("{tope}", tope.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) { Tope = tope };
    }
}
