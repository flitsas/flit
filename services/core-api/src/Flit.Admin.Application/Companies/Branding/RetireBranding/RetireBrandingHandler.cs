using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.RetireBranding;

/// <summary>
/// Retira la marca publicada (HU #12412 AC5/AC6): la fila se conserva, deja de resolverse. Exclusivo
/// SuperAdmin (no existe <c>POST /company/branding/retire</c>, contrato §4).
/// </summary>
public sealed class RetireBrandingHandler(ITenantBrandingRepository repository, IBrandingCacheInvalidator? cacheInvalidator = null)
{
    private readonly ITenantBrandingRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IBrandingCacheInvalidator _cacheInvalidator = cacheInvalidator ?? NullBrandingCacheInvalidator.Instance;

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

        // HU #12418 AC7 — retirar apaga la marca en /public/branding y /me/branding sin esperar los
        // 60 s de la caché de resolución.
        _cacheInvalidator.InvalidateTenant(command.TenantId);

        return RetireBrandingResult.Success(updated);
    }
}
