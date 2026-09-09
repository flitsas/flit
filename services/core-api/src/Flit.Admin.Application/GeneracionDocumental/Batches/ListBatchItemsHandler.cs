using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Batches;

/// <summary>
/// Filas de un lote, paginadas, para la tabla de seguimiento (CF-13 en la interfaz, HU #12211).
///
/// <para><b>La pertenencia del lote se comprueba primero.</b> Consultar directamente las filas por
/// <c>batch_id</c> con el tenant del JWT «también aislaría» —devolvería cero—, pero un lote ajeno y
/// un lote vacío darían la misma respuesta 200 con lista vacía, y de ahí se puede enumerar. Se
/// resuelve la cabecera y, si no es de la compañía, se responde <c>null</c> → 404 escueto.</para>
///
/// <para>Lo que devuelve es la proyección de <see cref="StandaloneDocumentBatchItem"/>: número de
/// fila, tipo, estado y el detalle del error con código y campo. <b>Nunca el valor capturado</b> que
/// produjo el error, ni snapshots, ni rutas de storage.</para>
/// </summary>
public sealed class ListBatchItemsHandler
{
    private readonly IStandaloneDocumentBatchRepository _batches;
    private readonly IStandaloneDocumentRepository _documents;

    public ListBatchItemsHandler(
        IStandaloneDocumentBatchRepository batches,
        IStandaloneDocumentRepository documents)
    {
        _batches = batches ?? throw new ArgumentNullException(nameof(batches));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    /// <summary><c>null</c> = el lote no existe o es de otra compañía (404 en el endpoint).</summary>
    public async Task<StandaloneDocumentBatchItemsPage?> HandleAsync(
        Guid tenantId,
        Guid batchId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var batch = await _batches.GetByIdAsync(tenantId, batchId, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return null;
        }

        return await _documents
            .ListBatchItemsAsync(tenantId, batchId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
