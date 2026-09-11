namespace Flit.Admin.Domain.Companies;

/// <summary>
/// Cliente hijo de una cabeza de grupo (GET <c>/{headTenantId}/children</c>).
/// No expone <c>ParentTenantId</c>: el padre ya va en la ruta.
/// </summary>
public sealed class CompanyChildListItem
{
    public Guid Id { get; init; }

    public string Nit { get; init; } = string.Empty;

    public string RazonSocial { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string TenantType { get; init; } = string.Empty;

    public bool EstadoActivo { get; init; }

    /// <summary>Fecha de vinculación a la cabeza (alta del hijo o último vínculo).</summary>
    public DateTimeOffset FechaVinculacion { get; init; }

    public long RowVersion { get; init; }
}
