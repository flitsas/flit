namespace Flit.Admin.Application.Companies.Domains.RemoveDomain;

/// <summary>Comando de <c>DELETE .../domain</c> — retiro exclusivo del SuperAdmin (HU #12416 AC5).</summary>
public sealed class RemoveDomainCommand
{
    public required Guid TenantId { get; init; }

    public Guid? ChangedBy { get; init; }
}
