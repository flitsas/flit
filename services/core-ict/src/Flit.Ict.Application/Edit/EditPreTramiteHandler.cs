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

    /// <summary>Precio fuera de rango (mismo criterio que el registro: &gt; 0, 2 decimales, 16 dígitos enteros).</summary>
    public const string InvalidSellingPrice = "invalid_selling_price";

    /// <summary>El pre-trámite existe en ICT pero core-api no encuentra el trámite materializado.</summary>
    public const string CoreApiNotFound = "core_api_not_found";

    /// <summary>core-api devolvió un error no previsto; su código interno NO se propaga al cliente.</summary>
    public const string CoreApiError = "core_api_error";

    /// <summary>
    /// Tope del precio de venta: core-api lo guarda en <c>numeric(18,2)</c> = 16 dígitos enteros + 2 decimales.
    /// </summary>
    public const decimal MaxSellingPrice = 9999999999999999.99m;

    /// <summary>Precio válido para ICT y para core-api: &gt; 0, máximo 2 decimales y 16 dígitos enteros.</summary>
    public static bool IsValidSellingPrice(decimal price) =>
        price is > 0m and <= MaxSellingPrice && decimal.Round(price, 2) == price;

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

        if (command.SellingPrice is { } requestedPrice && !IsValidSellingPrice(requestedPrice))
        {
            return (null, InvalidSellingPrice);
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
        if (!IsValidSellingPrice(price))
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
            return (null, MapCoreApiError(error));
        }

        var detail = JsonSerializer.Serialize(new
        {
            changed_fields = new[] { "selling_price" },
            validation_affecting = false,
        });

        // core-api ya aceptó el precio. Si el master cambió entre la lectura y el guardado (row_version), se
        // recarga, se reaplica y se reintenta UNA vez: el precio ya vive en FLIT y no debe quedar distinto aquí.
        if (!await TrySaveSellingPriceAsync(master, price, tenantId, ct))
        {
            var reloaded = await repository.GetAsync(master.Id, tenantId, ct);
            if (reloaded is null || !await TrySaveSellingPriceAsync(reloaded, price, tenantId, ct))
            {
                // Sin valores (el precio no va al timeline): solo deja constancia de que FLIT y ICT difieren.
                await repository.RecordTimelineEventAsync(master.Id, tenantId, "editado", "desincronizado", detail, ct);
                return (null, "stale");
            }
        }

        await repository.RecordTimelineEventAsync(master.Id, tenantId, "editado", "ok", detail, ct);

        return (new EditPreTramiteResult(master.Id, ValidationReset: false), null);
    }

    /// <summary>Aplica el precio al master y guarda; false si el guardado choca por row_version.</summary>
    private async Task<bool> TrySaveSellingPriceAsync(
        ExternalIntegrationMaster master, decimal price, Guid tenantId, CancellationToken ct)
    {
        master.SellingPrice = price;
        master.UpdatedBy = currentTenant.IntegrationClientId;
        try
        {
            await repository.SaveAsync(tenantId, ct);
            return true;
        }
        catch (IctConcurrencyException)
        {
            return false;
        }
    }

    /// <summary>
    /// Lista blanca de errores de core-api al editar el precio. Lo no previsto sale como
    /// <see cref="CoreApiError"/>: el código interno de FLIT no se expone al cliente ICT.
    /// </summary>
    internal static string MapCoreApiError(string? error) => error switch
    {
        NotDraft => NotDraft,
        "invalid_valor_venta" => InvalidSellingPrice,
        "not_found" => CoreApiNotFound,
        null or "" or "grpc_unavailable" => CoreApiUnavailable,
        _ => CoreApiError,
    };
}
