using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>Catálogo de reportes (R0, B0; contrato-api §2).</summary>
internal sealed record CatalogoReportesDto([property: JsonPropertyName("reportes")] IReadOnlyList<ReporteCatalogoDto> Reportes);

internal sealed record ReporteCatalogoDto(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("descripcion")] string Descripcion,
    [property: JsonPropertyName("contiene_pii")] bool ContienePii,
    [property: JsonPropertyName("fecha_referencia")] string FechaReferencia,
    [property: JsonPropertyName("filtros")] IReadOnlyList<FiltroCatalogoDto> Filtros,
    [property: JsonPropertyName("maximo_excel")] int MaximoExcel,
    [property: JsonPropertyName("maximo_pdf")] int MaximoPdf);

internal sealed record FiltroCatalogoDto(
    [property: JsonPropertyName("clave")] string Clave,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("repetible")] bool Repetible,
    [property: JsonPropertyName("valores")] IReadOnlyList<string> Valores);

/// <summary>Una columna de la lista blanca (contrato-api §4): clave, título, tipo de dato y si es PII.</summary>
internal sealed record ColumnaReporte(
    [property: JsonPropertyName("clave")] string Clave,
    [property: JsonPropertyName("titulo")] string Titulo,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("pii")] bool Pii);

/// <summary>
/// Página del reporte en pantalla (R1, B1, B4; contrato-api §3). Cada fila es un objeto cuyas claves son exactamente las de
/// <see cref="Columnas"/>, en su orden.
/// </summary>
internal sealed record PaginaReporteDto(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("ambito")] string Ambito,
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("acceso_id")] Guid AccesoId,
    [property: JsonPropertyName("columnas")] IReadOnlyList<ColumnaReporte> Columnas,
    [property: JsonPropertyName("filas")] IReadOnlyList<IReadOnlyDictionary<string, object?>> Filas);
