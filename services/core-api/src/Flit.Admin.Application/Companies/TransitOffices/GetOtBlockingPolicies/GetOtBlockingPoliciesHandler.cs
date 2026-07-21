using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.GetOtBlockingPolicies;

/// <summary>
/// Caso de uso de lectura de las políticas de bloqueo por OT de un tenant (FEATURE 05).
/// Tabla dispersa: lista vacía si el tenant no tiene ninguna fila (se aplican los defaults).
/// </summary>
public sealed class GetOtBlockingPoliciesHandler
{
    private readonly IOtBlockingPolicyRepository _repository;

    public GetOtBlockingPoliciesHandler(IOtBlockingPolicyRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<OtBlockingPolicyResponse>> HandleAsync(
        GetOtBlockingPoliciesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository
            .ListAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return [.. items.Select(i => new OtBlockingPolicyResponse(i.TransitOfficeId, i.Criterion, i.Blocks))];
    }
}
