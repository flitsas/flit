namespace Flit.Admin.Application.Companies.Domains.RegisterDomain;

/// <summary>Comando de <c>PUT .../domain</c> (HU #12416 AC1, AC5). <see cref="RowVersion"/> es opcional: ausente en el primer registro.</summary>
public sealed class RegisterDomainCommand
{
    public required Guid TenantId { get; init; }

    public string? Host { get; init; }

    public long? RowVersion { get; init; }

    public Guid? ChangedBy { get; init; }
}
