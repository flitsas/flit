using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerImpact;

/// <summary>
/// HU #13135 — impacto de dar de baja a un mandatario, de solo lectura. <c>null</c> si no existe, está eliminado o
/// no pertenece al organismo (404). Solo identificadores y conteos: nada de datos personales.
/// </summary>
public sealed class GetMandateSignerImpactHandler
{
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerImpactReader _impact;

    public GetMandateSignerImpactHandler(IMandateSignerReader reader, IMandateSignerImpactReader impact)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _impact = impact ?? throw new ArgumentNullException(nameof(impact));
    }

    public async Task<MandateSignerImpact?> HandleAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        CancellationToken cancellationToken = default)
    {
        var signer = await _reader.GetByIdAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
        if (signer is null
            || (signer.TransitOfficeId != transitOfficeId && !signer.TransitOfficeIds.Contains(transitOfficeId)))
        {
            return null;
        }

        return await _impact.GetImpactAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
    }
}
