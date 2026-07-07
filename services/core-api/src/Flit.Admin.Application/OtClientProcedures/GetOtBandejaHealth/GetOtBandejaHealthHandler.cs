using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures.GetOtBandejaHealth;

/// <summary>
/// Diagnóstico de la bandeja del OT (HU #10540 / R09): expone cuántos trámites entregados hacia el
/// organismo tienen grant vigente (visibles) y cuántos no (entregados huérfanos), para que el OT
/// corrija la configuración del grant en lugar de "perder" trámites reales silenciosamente.
/// </summary>
public sealed class GetOtBandejaHealthHandler
{
    private readonly IOtClientProcedureRepository _repository;

    public GetOtBandejaHealthHandler(IOtClientProcedureRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GetOtBandejaHealthResult> HandleAsync(
        GetOtBandejaHealthQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var health = await _repository.GetDeliveryHealthAsync(
            query.OtTenantId,
            query.TransitOfficeId,
            cancellationToken).ConfigureAwait(false);

        if (health is null)
        {
            return new GetOtBandejaHealthResult { TransitOfficeResolved = false };
        }

        return new GetOtBandejaHealthResult
        {
            TransitOfficeResolved = true,
            TransitOfficeId = health.TransitOfficeId,
            DeliveredTotal = health.DeliveredTotal,
            DeliveredWithGrant = health.DeliveredWithGrant,
            DeliveredWithoutGrant = health.DeliveredWithoutGrant,
        };
    }
}
