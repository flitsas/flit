namespace Flit.Admin.Application.GeneracionDocumental.GenerateRues;

/// <summary>
/// Orden de emisión del Certificado RUES standalone (CF-04). El tenant y el autor salen del JWT, no
/// del cuerpo de la petición: el documento queda SIEMPRE en el tenant del token, también para
/// SuperAdmin (CF-20).
/// </summary>
/// <param name="IdempotencyKey">
/// Cabecera <c>Idempotency-Key</c> (CF-16). Opcional; repetirla dentro del mismo tenant devuelve el
/// documento existente sin consultar al proveedor ni escribir un archivo nuevo.
/// </param>
/// <param name="BatchId">
/// Lote XLSX de origen (Feature #12201, I3). <c>null</c> en la generación individual. Viaja en el
/// comando —y no se asigna después— porque <c>batch_id</c> y <c>row_number</c> son INMUTABLES desde
/// el DDL 106: la capa 2 del trigger rechaza cualquier UPDATE que los toque, incluido pasarlos de
/// nulo a un valor. Quien crea la fila decide si es de lote.
/// </param>
/// <param name="RowNumber">Número de fila en el XLSX, 1-based sin encabezado. Obligatorio si hay lote.</param>
public sealed record GenerateRuesDocumentCommand(
    Guid TenantId,
    Guid UserId,
    string? Nit,
    string? IdempotencyKey = null,
    Guid? BatchId = null,
    int? RowNumber = null);

/// <summary>Desenlaces posibles de la generación. El endpoint los traduce a códigos HTTP.</summary>
public enum GenerateRuesDocumentOutcome
{
    /// <summary>Documento emitido (o recuperado por idempotencia). 200.</summary>
    Generated = 0,

    /// <summary>NIT ausente o con formato inválido. 400 — no se crea fila ni se consulta nada.</summary>
    InvalidRequest = 1,

    /// <summary>El NIT no tiene coincidencia en RUES. 422 — fila en error, sin archivo.</summary>
    RuesNotFound = 2,

    /// <summary>El proveedor no respondió. 502 — fila en error, sin archivo.</summary>
    ProviderUnavailable = 3,

    /// <summary>El proveedor no está registrado. 503 — fila en error, sin archivo.</summary>
    ProviderNotFound = 4,
}

/// <summary>
/// Respuesta de la generación. <b>Nunca transporta el PDF</b> (decisión del PO): el binario se
/// descarga después por <c>GET /{id}/download</c>.
/// </summary>
/// <param name="Status">
/// Solo <c>generated</c> o <c>error</c> (CF-21): <c>pending</c> y <c>processing</c> son estados de
/// tránsito de la fila y no se responden en la generación individual.
/// </param>
public sealed record GenerateRuesDocumentResult(
    GenerateRuesDocumentOutcome Outcome,
    Guid? Id,
    string? Status,
    string? ErrorCode);
