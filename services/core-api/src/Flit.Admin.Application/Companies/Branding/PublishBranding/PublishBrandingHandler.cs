using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.PublishBranding;

/// <summary>
/// Publica el borrador vigente (HU #12412 AC4): operación explícita y separada de guardar el
/// borrador. La completitud (AC6 de #12413) la evalúa <see cref="IBrandAssetValidator"/> — en esta HU
/// nunca bloquea (validador permisivo), así que <see cref="PublishBrandingOutcome.Incomplete"/> queda
/// cableado pero inalcanzable hasta que #12413 registre la implementación real.
/// </summary>
public sealed class PublishBrandingHandler(
    ITenantBrandingRepository repository,
    IBrandAssetValidator validator,
    IBrandingCacheInvalidator? cacheInvalidator = null)
{
    private readonly ITenantBrandingRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IBrandAssetValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IBrandingCacheInvalidator _cacheInvalidator = cacheInvalidator ?? NullBrandingCacheInvalidator.Instance;

    public async Task<PublishBrandingResult> HandleAsync(
        PublishBrandingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var current = await _repository.GetByTenantIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return PublishBrandingResult.NotFound();
        }

        if (command.RowVersion is { } expected && expected != current.RowVersion)
        {
            return PublishBrandingResult.Conflict();
        }

        // Presencia estructural del borrador (no formato/contraste, eso es #12413 AC1-AC5). El
        // validador permisivo no aporta reglas adicionales de completitud en esta HU.
        var missing = current.Draft.MissingFields();
        if (missing.Count > 0 && _validator.ValidateDraft(current.Draft).Count > 0)
        {
            return PublishBrandingResult.Incomplete(missing);
        }

        var updated = await _repository
            .PublishAsync(command.TenantId, command.ChangedBy, cancellationToken)
            .ConfigureAwait(false);

        // HU #12418 AC7 — la marca nueva debe verse en /public/branding y /me/branding sin esperar
        // los 60 s de la caché de resolución.
        _cacheInvalidator.InvalidateTenant(command.TenantId);

        return PublishBrandingResult.Success(updated);
    }
}
