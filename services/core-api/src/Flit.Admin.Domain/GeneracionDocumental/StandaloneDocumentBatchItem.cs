namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Fila de un lote XLSX tal como la ve el seguimiento (CF-13 en la interfaz, HU #12211).
///
/// <para><b>Es una proyección deliberadamente pobre en PII</b>, como la del historial: número de
/// fila, tipo, escenario, estado y el detalle del error. Nunca <c>document_snapshot</c>,
/// <c>rues_snapshot</c> ni <c>storage_path</c>. Y <see cref="ValidationErrors"/> trae
/// <c>[{code, field, message}]</c> —el campo que falló, no su contenido—: mostrar el valor
/// capturado enseñaría una cédula o una dirección a cualquiera con permiso de lectura.</para>
///
/// <para><b>Trampa del esquema, heredada de HU #12210:</b> los CHECK de
/// <c>admin.standalone_documents</c> solo admiten dos literales en <c>document_type</c>. Una fila
/// cuyo tipo el usuario tecleó mal —o una transferencia sin escenario válido— no se puede tipificar
/// y se persiste como <c>certificado_rues</c> con escenario nulo, con el error real en
/// <see cref="ValidationErrors"/>. En esas filas <see cref="DocumentType"/> MIENTE: la interfaz debe
/// mostrar el error, no la columna. Corregirlo exige un DDL nuevo y es decisión del PO.</para>
/// </summary>
public sealed record StandaloneDocumentBatchItem
{
    public required Guid Id { get; init; }

    /// <summary>Número de la fila en el XLSX (1 = primera fila de datos, sin contar el encabezado).</summary>
    public int? RowNumber { get; init; }

    /// <summary>Uno de <see cref="StandaloneDocumentType"/>. Ver la advertencia de la clase.</summary>
    public required string DocumentType { get; init; }

    public string? Scenario { get; init; }

    /// <summary>Estado INTERNO (los cuatro de <see cref="StandaloneDocumentStatus"/>).</summary>
    public required string Status { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorField { get; init; }

    /// <summary>JSON <c>[{code, field, message}]</c>. Sin valores capturados (CF-13).</summary>
    public string ValidationErrors { get; init; } = "[]";

    public string? Filename { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Página de filas de un lote: ítems + coordenadas de paginación.</summary>
public sealed record StandaloneDocumentBatchItemsPage(
    IReadOnlyList<StandaloneDocumentBatchItem> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// Una entrada del ZIP del lote (CF-15): el nombre con el que entra al archivo y la ruta opaca del
/// binario en storage.
///
/// <para><b>Solo se materializa para filas en <c>generated</c>.</b> La consulta que la produce
/// filtra por estado en SQL, así que una fila en error no llega hasta aquí: no hay «filtrar después»
/// que se pueda olvidar. <see cref="StoragePath"/> NO sale nunca en una respuesta HTTP; solo lo usa
/// el streamer para abrir el binario.</para>
/// </summary>
public sealed record StandaloneDocumentBatchZipEntry(int? RowNumber, string Filename, string StoragePath);
