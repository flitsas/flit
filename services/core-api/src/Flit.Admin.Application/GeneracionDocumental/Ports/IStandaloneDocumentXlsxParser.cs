using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>
/// Una fila de datos del XLSX ya normalizada. <see cref="RowNumber"/> es 1-based y NO cuenta el
/// encabezado: es el número que se le enseña al usuario y el que va a
/// <c>admin.standalone_documents.row_number</c>.
///
/// <para><see cref="Values"/> está indexado por el encabezado de la plantilla v1, no por posición:
/// las celdas vacías del XLSX se OMITEN del XML y contar posiciones desalinea la fila entera.</para>
///
/// <para><see cref="Errors"/> trae los errores detectados al leer (p. ej. un serial numérico en una
/// columna de fecha). Una fila con errores <b>no cancela el lote</b> (CF-13): se persiste en
/// <c>error</c> y el worker sigue con la siguiente.</para>
/// </summary>
public sealed record StandaloneBatchParsedRow(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyList<StandaloneDocumentValidationError> Errors);

/// <summary>
/// Resultado de leer el archivo cargado. <see cref="ErrorCode"/> no nulo significa que el archivo
/// ENTERO se rechaza y no se persiste nada (ni lote, ni filas, ni el XLSX en storage).
/// </summary>
public sealed record StandaloneBatchParseResult(
    string? ErrorCode,
    IReadOnlyList<StandaloneBatchParsedRow> Rows)
{
    public static StandaloneBatchParseResult Rejected(string errorCode) => new(errorCode, []);
}

/// <summary>
/// Códigos de rechazo del archivo completo (CF-11). Son del contrato HTTP: viajan tal cual en el
/// 422 y el frontend los traduce.
/// </summary>
public static class StandaloneBatchParseError
{
    /// <summary>El binario no es un XLSX real (MIME verificado por contenido, no por extensión).</summary>
    public const string InvalidFile = "invalid_file";

    /// <summary>El encabezado no es el de la plantilla v1: falta, sobra o está en otro orden.</summary>
    public const string TemplateInvalid = "template_invalid";

    /// <summary>Más de <see cref="Batches.StandaloneBatchTemplate.MaxRows"/> filas de datos.</summary>
    public const string TooManyRows = "too_many_rows";
}

/// <summary>
/// Puerto acotado del lector de la plantilla XLSX v1 (Feature #12201, I3).
///
/// <para><b>Sin ClosedXML ni EPPlus</b> (§8.4 del diseño y regla FLIT 18: una dependencia nueva
/// exige auditoría previa). El adaptador lee <c>sheet1.xml</c> y <c>sharedStrings.xml</c> con
/// <c>OpenXmlReader</c> en modo SAX, que ya está en el repo vía <c>DocumentFormat.OpenXml</c>.</para>
///
/// <para>El puerto vive en Application porque el worker y el handler de carga no pueden nombrar
/// tipos de OpenXml —ni de <c>Flit.Tramites.*</c>— (restricción C6 del ADR-0056).</para>
/// </summary>
public interface IStandaloneDocumentXlsxParser
{
    /// <summary>
    /// Lee el archivo cargado. <b>No lanza</b> ante un binario corrupto: devuelve
    /// <see cref="StandaloneBatchParseError.InvalidFile"/>, porque un archivo del usuario no es una
    /// condición excepcional del servidor.
    /// </summary>
    StandaloneBatchParseResult Parse(Stream xlsx);
}

/// <summary>
/// Puerto del generador de la plantilla vacía que se descarga por <c>GET /lotes/plantilla</c>
/// (CF-11). Emite <b>todas</b> las columnas como texto, que es lo que hace tratable el parser.
/// </summary>
public interface IStandaloneDocumentXlsxTemplate
{
    RenderedStandaloneDocument Build();
}
