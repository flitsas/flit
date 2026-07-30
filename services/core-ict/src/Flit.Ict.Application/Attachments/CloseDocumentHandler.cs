using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Attachments;

/// <summary>
/// Cierre del documento de un pre-trámite (paridad v1: el cliente envía closed=true = "ya no subiré más
/// adjuntos"). Marca <c>closed_document=true</c>; a partir de ahí el pre-trámite deja de ESPERAR y, si pasó
/// negocio + fuentes externas sin novedades, la materialización (<see cref="Jobs"/> SendToCoreApiJob) lo
/// lleva a borrador. Mientras el documento siga abierto y sin el waiver
/// (<c>process_without_attached_documents</c>), el pre-trámite se queda esperando (no materializa).
/// Idempotente: cerrar dos veces devuelve ok. No se puede cerrar un pre-trámite ya materializado.
/// El upload v1 con closed=true (<see cref="UploadAttachmentV1Handler"/>) fija el mismo flag; este handler
/// permite cerrar SIN adjuntar un archivo más (flujo v2 presign→register, o cierre tras el último adjunto).
/// </summary>
public sealed class CloseDocumentHandler(
    IPreTramiteRepository preTramites,
    ICurrentTenant currentTenant)
{
    public async Task<(bool Ok, string? Error)> HandleAsync(string managerIdTransaction, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(managerIdTransaction))
        {
            return (false, "invalid_request");
        }

        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (false, "unauthenticated");
        }

        var master = await preTramites.FindByManagerIdTransactionAsync(managerIdTransaction, tenantId.Value, ct);
        if (master is null)
        {
            return (false, "not_found");
        }

        // Ya hay borrador: cerrar no tiene sentido (el pipeline ICT ya no gobierna el trámite).
        if (master.ProcedureInstanceId is not null)
        {
            return (false, "already_materialized");
        }

        // Idempotente: si ya estaba cerrado, no reescribe ni re-emite evento.
        if (master.ClosedDocument)
        {
            return (true, null);
        }

        master.ClosedDocument = true;
        master.UpdatedBy = currentTenant.IntegrationClientId;
        await preTramites.SaveAsync(tenantId.Value, ct);

        await preTramites.RecordTimelineEventAsync(
            master.Id, tenantId.Value, "documento_cerrado", "ok", null, ct);

        return (true, null);
    }
}
