using System.Text.Json;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Application.Edit;

/// <summary>
/// Edita un pre-trámite mientras sea editable (antes de iniciar la validación externa y antes de
/// materializar el borrador). Concurrencia optimista por row_version; reset selectivo de validaciones
/// si cambia un campo "validation-affecting".
/// <para>
/// Bug #13304: ya materializado, el ÚNICO campo editable es <c>selling_price</c>, y solo mientras el
/// trámite siga en borrador en core-api (lo decide core-api: <c>not_draft</c>). El resto sigue con
/// <c>already_materialized</c>.
/// </para>
/// </summary>
public sealed class EditPreTramiteHandler(
    IPreTramiteRepository repository,
    ICurrentTenant currentTenant,
    IProcedureDraftClient draftClient)
{
    /// <summary>El trámite ya salió de borrador en core-api: el precio ya no se puede editar.</summary>
    public const string NotDraft = "not_draft";

    /// <summary>core-api no respondió (canal gRPC caído); el cliente puede reintentar.</summary>
    public const string CoreApiUnavailable = "core_api_unavailable";

    /// <summary>Precio fuera de rango (mismo criterio que el registro: &gt; 0, 2 decimales, 17 dígitos).</summary>
    public const string InvalidSellingPrice = "invalid_selling_price";

    public async Task<(EditPreTramiteResult? Result, string? Error)> HandleAsync(
        EditPreTramiteCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (null, "unauthenticated");
        }

        var master = await repository.GetAsync(command.Id, tenantId.Value, ct);
        if (master is null)
        {
            return (null, "not_found");
        }

        // Una vez materializado en core-api, ICT ya no gobierna la edición, salvo el precio de venta mientras
        // el trámite siga en borrador (Bug #13304).
        if (master.ProcedureInstanceId is { } procedureInstanceId)
        {
            return IsOnlySellingPrice(command)
                ? await EditMaterializedSellingPriceAsync(master, procedureInstanceId, command, tenantId.Value, ct)
                : (null, "already_materialized");
        }

        // Corte conservador: no editar mientras un tercero valida (external_validation iniciada).
        if (master.ExternalValidation != 0)
        {
            return (null, "not_editable");
        }

        if (master.RowVersion != command.RowVersion)
        {
            return (null, "stale");
        }

        var validationAffectingChanged = false;
        // Solo NOMBRES de campos cambiados para el timeline (nunca los valores nuevos: manager_mail
        // y delivery_address son PII).
        var changedFields = new List<string>();

        if (command.DeliveryAddress is not null)
        {
            master.DeliveryAddress = command.DeliveryAddress.Trim();
            changedFields.Add("delivery_address");
        }

        if (command.ManagerMail is not null)
        {
            master.ManagerMail = command.ManagerMail.Trim();
            changedFields.Add("manager_mail");
        }

        if (command.SellingDate is not null && command.SellingDate.Trim() != master.SellingDate)
        {
            master.SellingDate = command.SellingDate.Trim();
            validationAffectingChanged = true;
            changedFields.Add("selling_date");
        }

        if (command.SellingPrice is { } price && price != master.SellingPrice)
        {
            master.SellingPrice = price;
            validationAffectingChanged = true;
            changedFields.Add("selling_price");
        }

        if (command.TrafficSecretaryCode is not null && command.TrafficSecretaryCode.Trim() != master.TrafficSecretaryCode)
        {
            master.TrafficSecretaryCode = command.TrafficSecretaryCode.Trim();
            validationAffectingChanged = true;
            changedFields.Add("traffic_secretary_code");
        }

        if (command.ProcessWithoutAttachedDocuments is { } flag && flag != master.ProcessWithoutAttachedDocuments)
        {
            master.ProcessWithoutAttachedDocuments = flag;
            validationAffectingChanged = true;
            changedFields.Add("process_without_attached_documents");
        }

        if (validationAffectingChanged)
        {
            master.BusinessValidation = 0;
            master.ExternalValidation = 0;
            master.ProcessStatusId = 1;
            master.BusinessCommentsValidation = string.Empty;
            master.ExternalCommentsValidation = string.Empty;
        }

        master.UpdatedBy = currentTenant.IntegrationClientId;

        try
        {
            await repository.SaveAsync(tenantId.Value, ct);
        }
        catch (IctConcurrencyException)
        {
            return (null, "stale");
        }

        var detail = JsonSerializer.Serialize(new
        {
            changed_fields = changedFields,
            validation_affecting = validationAffectingChanged,
        });
        await repository.RecordTimelineEventAsync(master.Id, tenantId.Value, "editado", "ok", detail, ct);

        return (new EditPreTramiteResult(master.Id, validationAffectingChanged), null);
    }

    /// <summary>El comando trae selling_price y ningún otro campo editable.</summary>
    private static bool IsOnlySellingPrice(EditPreTramiteCommand c) =>
        c.SellingPrice is not null
        && c.DeliveryAddress is null
        && c.ManagerMail is null
        && c.SellingDate is null
        && c.TrafficSecretaryCode is null
        && c.ProcessWithoutAttachedDocuments is null;

    /// <summary>
    /// Precio de venta de un pre-trámite ya materializado: se aplica primero en core-api (dueño del
    /// borrador) y solo si lo acepta se refleja en el master. NO resetea validaciones (ya pasaron y el
    /// trámite vive en FLIT). Se envía aunque el precio coincida con el del master: es la vía para
    /// reponer el comercial que core-api no pudo guardar al materializar (commercial_warning).
    /// </summary>
    private async Task<(EditPreTramiteResult? Result, string? Error)> EditMaterializedSellingPriceAsync(
        ExternalIntegrationMaster master,
        Guid procedureInstanceId,
        EditPreTramiteCommand command,
        Guid tenantId,
        CancellationToken ct)
    {
        var price = command.SellingPrice!.Value;
        if (price is <= 0m or > 99999999999999999m || decimal.Round(price, 2) != price)
        {
            return (null, InvalidSellingPrice);
        }

        if (master.RowVersion != command.RowVersion)
        {
            return (null, "stale");
        }

        var (ok, error) = await draftClient.UpdateCommercialAsync(tenantId, procedureInstanceId, master.Id, price, ct);
        if (!ok)
        {
            return (null, error switch
            {
                NotDraft => NotDraft,
                null or "" or "grpc_unavailable" => CoreApiUnavailable,
                _ => error,
            });
        }

        master.SellingPrice = price;
        master.UpdatedBy = currentTenant.IntegrationClientId;

        try
        {
            await repository.SaveAsync(tenantId, ct);
        }
        catch (IctConcurrencyException)
        {
            return (null, "stale");
        }

        var detail = JsonSerializer.Serialize(new
        {
            changed_fields = new[] { "selling_price" },
            validation_affecting = false,
        });
        await repository.RecordTimelineEventAsync(master.Id, tenantId, "editado", "ok", detail, ct);

        return (new EditPreTramiteResult(master.Id, ValidationReset: false), null);
    }
}
