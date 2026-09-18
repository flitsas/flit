using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;

/// <summary>
/// Crea o reemplaza el borrador de marca (HU #12412 AC1/AC4). Concurrencia optimista por
/// <c>rowVersion</c> (patrón <c>UpdateCompanyHandler</c>): si el cliente envía la versión que leyó y ya
/// no coincide, 409 sin persistir nada. El disparador de BD (AC2) se traduce a
/// <see cref="UpsertBrandingDraftOutcome.TenantNotMarcaBlanca"/>.
/// </summary>
public sealed class UpsertBrandingDraftHandler(
    ITenantBrandingRepository repository,
    IBrandAssetValidator validator)
{
    private readonly ITenantBrandingRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IBrandAssetValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public async Task<UpsertBrandingDraftResult> HandleAsync(
        UpsertBrandingDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        var colors = request.Colors is null
            ? null
            : new BrandColors(
                Normalize(request.Colors.Primary),
                Normalize(request.Colors.Secondary),
                Normalize(request.Colors.OnPrimary));

        var draft = new BrandingDraft(request.PlatformName?.Trim(), colors, request.LogoId);

        var errors = _validator.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            return UpsertBrandingDraftResult.Invalid(errors);
        }

        var current = await _repository.GetByTenantIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (current is not null && request.RowVersion is { } expected && expected != current.RowVersion)
        {
            return UpsertBrandingDraftResult.Conflict();
        }

        TenantBranding updated;
        try
        {
            updated = await _repository
                .UpsertDraftAsync(command.TenantId, draft, command.ChangedBy, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BrandingTenantNotMarcaBlancaException)
        {
            return UpsertBrandingDraftResult.TenantNotMarcaBlanca();
        }

        return UpsertBrandingDraftResult.Success(updated);
    }

    private static string Normalize(string hex) => hex?.Trim().ToUpperInvariant() ?? string.Empty;
}
