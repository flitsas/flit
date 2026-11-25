using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Documents;

/// <summary>
/// HU #14668 Bloque 2 — entrega la URL prefirmada del consolidado PDF de un trámite aprobado.
/// <list type="number">
///   <item>Resuelve el pre-trámite por <c>id</c> (transaction_number o manager_id_transaction).</item>
///   <item>Verifica que esté materializado (<c>procedure_instance_id</c> != null).</item>
///   <item>Lee el estado v2 del trámite en <c>tramites.procedure_instances.status</c>; solo
///       continúa si es <c>aprobado</c> (estado final OT que habilita la descarga del consolidado).</item>
///   <item>Obtiene la presigned URL del consolidado vía <see cref="IIctConsolidadoClient"/>.</item>
/// </list>
/// </summary>
public sealed class GetDocumentsTransactionHandler(
    IPreTramiteRepository repository,
    IIctConsolidadoClient consolidadoClient,
    ICurrentTenant currentTenant)
{
    /// <summary>
    /// Maneja la solicitud de URL del consolidado.
    /// </summary>
    /// <param name="id">
    /// Referencia del pre-trámite: número secuencial (<c>transaction_number</c>) o
    /// <c>manager_id_transaction</c> del gestor.
    /// </param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>
    /// <c>(url, null)</c> cuando todo OK.<br/>
    /// <c>(null, errorCode)</c> con uno de los siguientes códigos:
    /// <list type="bullet">
    ///   <item><c>unauthenticated</c> — sin tenant en el token.</item>
    ///   <item><c>not_found</c> — no existe el pre-trámite para ese tenant.</item>
    ///   <item><c>not_materialized</c> — el pre-trámite no llegó a generar borrador en FLIT.</item>
    ///   <item><c>not_approved</c> — el trámite existe pero no está en estado <c>aprobado</c>.</item>
    ///   <item><c>consolidado_no_disponible</c> — el trámite aprobado no tiene consolidado generado.</item>
    ///   <item><c>storage_unavailable</c> — el File Manager no pudo emitir la URL.</item>
    /// </list>
    /// </returns>
    public async Task<(string? Url, string? Error)> HandleAsync(string id, CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (null, "unauthenticated");
        }

        var master = await repository.FindByManagerIdTransactionAsync(id, tenantId.Value, ct);
        if (master is null)
        {
            return (null, "not_found");
        }

        if (master.ProcedureInstanceId is null)
        {
            return (null, "not_materialized");
        }

        // Solo continuar si el trámite está aprobado (único estado final que habilita descarga del OT).
        var tramiteStatus = await repository.GetTramiteStatusAsync(master.ProcedureInstanceId.Value, ct);
        if (!string.Equals(tramiteStatus, "aprobado", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "not_approved");
        }

        return await consolidadoClient.GetPresignedUrlAsync(tenantId.Value, master.ProcedureInstanceId.Value, ct);
    }
}
