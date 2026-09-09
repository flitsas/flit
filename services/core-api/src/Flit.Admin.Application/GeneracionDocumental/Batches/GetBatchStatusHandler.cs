using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Batches;

/// <summary>
/// Avance de un lote para el seguimiento in-app (CF-14/CF-21, HU #12211). Es lo que el frontend
/// consulta cada 4 segundos: barato, tenant-scoped y sin una sola línea de PII.
///
/// <para><b>Los contadores se cuentan, no se leen de la cabecera.</b>
/// <c>admin.standalone_document_batches</c> solo escribe <c>generated_count</c> y
/// <c>error_count</c> al CERRAR el lote: leerlos ahí dejaría el progreso clavado en 0/100 durante
/// todo el procesamiento y el usuario vería una barra muerta. Se cuentan las filas ya
/// materializadas, que es el avance real.</para>
///
/// <para><b>Cross-tenant es 404, no 403</b> (CF-20/R3): un 403 confirmaría que el lote existe y
/// permitiría enumerar los de otras compañías. El control es este filtro por tenant en el
/// repositorio más la autorización del endpoint, nunca la RLS —que en este repo no aísla nada.</para>
/// </summary>
public sealed class GetBatchStatusHandler
{
    private readonly IStandaloneDocumentBatchRepository _batches;
    private readonly IStandaloneDocumentRepository _documents;

    public GetBatchStatusHandler(
        IStandaloneDocumentBatchRepository batches,
        IStandaloneDocumentRepository documents)
    {
        _batches = batches ?? throw new ArgumentNullException(nameof(batches));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    /// <summary>
    /// Devuelve el avance del lote, o <c>null</c> si no existe o pertenece a otra compañía —el
    /// endpoint traduce ese <c>null</c> a un 404 escueto, idéntico en ambos casos.
    /// </summary>
    public async Task<StandaloneBatchStatusView?> HandleAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _batches.GetByIdAsync(tenantId, batchId, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return null;
        }

        var filas = await _documents
            .ListBatchRowStatesAsync(tenantId, batchId, cancellationToken)
            .ConfigureAwait(false);

        var generados = filas.Count(f => f.Status == StandaloneDocumentStatus.Generated);
        var errores = filas.Count(f => f.Status == StandaloneDocumentStatus.Error);

        return new StandaloneBatchStatusView(
            batch.Id,
            batch.Status,
            batch.TotalItems,
            generados,
            errores,
            filas.Count,
            StandaloneDocumentBatchStatus.IsTerminal(batch.Status),
            batch.CreatedAt,
            batch.CompletedAt);
    }
}

/// <summary>
/// Avance del lote tal como sale por HTTP. Sin nombre de archivo fuente, sin ruta de storage y sin
/// nada del contenido del XLSX: el archivo trae los datos completos de las partes.
///
/// <para><see cref="IsTerminal"/> viaja EXPLÍCITO y no se deduce en el cliente: es la señal con la
/// que el polling se detiene (CF-14). Si el contrato gana un estado terminal nuevo, el frontend deja
/// de sondear sin necesidad de una versión nueva.</para>
/// </summary>
public sealed record StandaloneBatchStatusView(
    Guid BatchId,
    string Status,
    int Total,
    int Generated,
    int Errors,
    int Processed,
    bool IsTerminal,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
