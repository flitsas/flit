namespace Flit.Admin.Application.Companies.Branding.PublishBranding;

public sealed class PublishBrandingCommand
{
    public required Guid TenantId { get; init; }

    public long? RowVersion { get; init; }

    public Guid? ChangedBy { get; init; }
}
