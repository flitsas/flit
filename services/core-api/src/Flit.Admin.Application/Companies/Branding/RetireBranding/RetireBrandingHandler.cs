using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.RetireBranding;

/// <summary>
/// Retira la marca publicada (HU #12412 AC5/AC6): la fila se conserva, deja de resolverse. Exclusivo
/// SuperAdmin (no existe <c>POST /company/branding/retire</c>, contrato §4).
/// </summary>
public sealed class RetireBrandingHandler(ITenantBrandingRepository repository)
{
    private readonly ITenantBrandingRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<RetireBrandingResult> HandleAsync(
        RetireBrandingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var current = await _repository.GetByTenantIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return RetireBrandingResult.NotFound();
        }

        var updated = await _repository
            .RetireAsync(command.TenantId, command.ChangedBy, cancellationToken)
            .ConfigureAwait(false);

        return RetireBrandingResult.Success(updated);
    }
}
