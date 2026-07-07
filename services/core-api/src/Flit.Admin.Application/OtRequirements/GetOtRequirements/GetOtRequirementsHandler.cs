using Flit.Admin.Domain.OtRequirements;

namespace Flit.Admin.Application.OtRequirements.GetOtRequirements;

/// <summary>
/// Obtiene los requisitos configurables del OT del tenant (HU #10546 / AC lectura). Si el OT no
/// tiene fila, el provider devuelve <see cref="OtRequirements.SafeDefaults"/> — no auto-persiste.
/// </summary>
public sealed class GetOtRequirementsHandler
{
    private readonly IOtRequirementsProvider _provider;

    public GetOtRequirementsHandler(IOtRequirementsProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<OtRequirementsResponse> HandleAsync(
        GetOtRequirementsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requirements = await _provider
            .ResolveByTenantAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return OtRequirementsMapper.ToResponse(requirements);
    }
}
