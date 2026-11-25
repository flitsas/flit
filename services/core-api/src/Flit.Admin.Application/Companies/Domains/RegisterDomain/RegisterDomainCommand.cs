namespace Flit.Admin.Application.Companies.Domains.RegisterDomain;

/// <summary>Comando de <c>PUT .../domain</c> (HU #12416 AC1, AC5). <see cref="RowVersion"/> es opcional: ausente en el primer registro.</summary>
public sealed class RegisterDomainCommand
{
    public required Guid TenantId { get; init; }

    /// <summary><c>HUB</c> o el código del producto que servirá el dominio.</summary>
    public string Purpose { get; init; } = Flit.Admin.Domain.Companies.Domains.TenantDomainPurposes.Hub;

    public string? Host { get; init; }

    public long? RowVersion { get; init; }

    public Guid? ChangedBy { get; init; }
}
