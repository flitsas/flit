namespace Flit.Admin.Application.Companies.Branding.RetireBranding;

public sealed class RetireBrandingCommand
{
    public required Guid TenantId { get; init; }

    public Guid? ChangedBy { get; init; }
}
