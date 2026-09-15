namespace Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;

public sealed class UpsertBrandingDraftCommand
{
    public required Guid TenantId { get; init; }

    public required UpsertBrandingDraftRequest Request { get; init; }

    public Guid? ChangedBy { get; init; }
}
