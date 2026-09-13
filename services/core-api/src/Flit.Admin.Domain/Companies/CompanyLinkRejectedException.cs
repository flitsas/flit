namespace Flit.Admin.Domain.Companies;

/// <summary>
/// HU #12355 — la base rechazó vincular/desvincular (tipo de cabeza, padre inválido, etc.).
/// </summary>
public sealed class CompanyLinkRejectedException : Exception
{
    public CompanyLinkRejectedException(Guid childTenantId, string reason)
        : base(reason)
    {
        ChildTenantId = childTenantId;
    }

    public Guid ChildTenantId { get; }
}
