using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.GetOtConsultationRestrictions;

/// <summary>
/// Caso de uso de lectura de las restricciones de consulta por OT de un tenant (HU #10759,
/// AC1/AC5). Tabla dispersa: lista vacía si el tenant no tiene ninguna fila (permisivo).
/// </summary>
public sealed class GetOtConsultationRestrictionsHandler
{
    private readonly IOtConsultationRestrictionRepository _repository;

    public GetOtConsultationRestrictionsHandler(IOtConsultationRestrictionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<OtConsultationRestrictionResponse>> HandleAsync(
        GetOtConsultationRestrictionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository
            .ListAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return [.. items.Select(i => new OtConsultationRestrictionResponse(i.TransitOfficeId, i.ConsultationKind, i.Enabled))];
    }
}
