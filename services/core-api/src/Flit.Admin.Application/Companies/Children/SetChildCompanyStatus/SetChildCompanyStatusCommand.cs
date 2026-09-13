namespace Flit.Admin.Application.Companies.Children.SetChildCompanyStatus;

public sealed class SetChildCompanyStatusCommand
{
    public required Guid HeadTenantId { get; init; }

    public required Guid ChildTenantId { get; init; }

    public required bool EstadoActivo { get; init; }

    public Guid? ChangedBy { get; init; }
}
