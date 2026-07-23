using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Edit;

/// <summary>
/// Edita un pre-trámite mientras sea editable (antes de iniciar la validación externa y antes de
/// materializar el borrador). Concurrencia optimista por row_version; reset selectivo de validaciones
/// si cambia un campo "validation-affecting".
/// </summary>
public sealed class EditPreTramiteHandler(IPreTramiteRepository repository, ICurrentTenant currentTenant)
{
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

        // Una vez materializado en core-api, ICT ya no gobierna la edición.
        if (master.ProcedureInstanceId is not null)
        {
            return (null, "already_materialized");
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

        if (command.DeliveryAddress is not null)
        {
            master.DeliveryAddress = command.DeliveryAddress.Trim();
        }

        if (command.ManagerMail is not null)
        {
            master.ManagerMail = command.ManagerMail.Trim();
        }

        if (command.SellingDate is not null && command.SellingDate.Trim() != master.SellingDate)
        {
            master.SellingDate = command.SellingDate.Trim();
            validationAffectingChanged = true;
        }

        if (command.SellingPrice is { } price && price != master.SellingPrice)
        {
            master.SellingPrice = price;
            validationAffectingChanged = true;
        }

        if (command.TrafficSecretaryCode is not null && command.TrafficSecretaryCode.Trim() != master.TrafficSecretaryCode)
        {
            master.TrafficSecretaryCode = command.TrafficSecretaryCode.Trim();
            validationAffectingChanged = true;
        }

        if (command.ProcessWithoutAttachedDocuments is { } flag && flag != master.ProcessWithoutAttachedDocuments)
        {
            master.ProcessWithoutAttachedDocuments = flag;
            validationAffectingChanged = true;
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

        return (new EditPreTramiteResult(master.Id, validationAffectingChanged), null);
    }
}
