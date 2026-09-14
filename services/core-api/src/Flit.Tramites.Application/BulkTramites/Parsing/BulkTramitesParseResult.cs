namespace Flit.Tramites.Application.BulkTramites.Parsing;

/// <summary>Motivo de rechazo del ARCHIVO completo (AC2). Distinto del error por fila (por fila, ver <see cref="BulkTramitesParsedRow"/>).</summary>
public static class BulkTramitesFileError
{
    /// <summary>No es un XLSX válido (firma ZIP ausente, o no se pudo abrir como paquete OPC).</summary>
    public const string InvalidFile = "invalid_file";

    /// <summary>El encabezado no coincide con el de la plantilla del tipo declarado.</summary>
    public const string TemplateInvalid = "template_invalid";

    /// <summary>Más de 50 filas de datos.</summary>
    public const string TooManyRows = "too_many_rows";
}

/// <summary>
/// Resultado de parsear un archivo de carga masiva. <see cref="FileError"/> nulo significa que el
/// archivo respeta la estructura de su plantilla — no que todas sus filas sean válidas: revisar
/// <see cref="BulkTramitesParsedRow.StructuralErrorCode"/> fila a fila.
/// </summary>
public sealed record BulkTramitesParseResult(string? FileError, IReadOnlyList<BulkTramitesParsedRow> Rows)
{
    public static BulkTramitesParseResult Rejected(string fileError) => new(fileError, []);
}
