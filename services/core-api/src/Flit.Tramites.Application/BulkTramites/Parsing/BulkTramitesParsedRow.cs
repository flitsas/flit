namespace Flit.Tramites.Application.BulkTramites.Parsing;

/// <summary>
/// Una fila ya parseada del archivo de carga masiva. <see cref="RowNumber"/> es 1-based y no
/// cuenta el encabezado — el mismo número que ve el usuario en el resumen del lote (HU #12524).
///
/// <para><see cref="Values"/> está indexado por el encabezado de la plantilla, no por posición:
/// las celdas vacías del XLSX se OMITEN del XML y contar posiciones desalinea la fila entera.</para>
///
/// <para><see cref="StructuralErrorCode"/> es el único error que detecta HU #12522 (hoy, solo
/// reglas de porcentaje de propiedad en Traspaso). Una fila con error estructural NO cancela el
/// lote (AC3): se persiste marcada y queda fuera de la cola de HU #12523.</para>
/// </summary>
public sealed record BulkTramitesParsedRow(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values,
    string? StructuralErrorCode);
