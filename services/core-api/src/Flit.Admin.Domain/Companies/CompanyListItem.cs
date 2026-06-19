namespace Flit.Admin.Domain.Companies;

/// <summary>
/// Read model de una compañía B2B para el listado administrativo multi-tenant.
/// Proyección sobre <c>identity.tenants</c> (HU #10189, RF02).
/// </summary>
public sealed class CompanyListItem
{
    public Guid Id { get; init; }

    /// <summary>NIT — <c>identity.tenants.tax_id</c>.</summary>
    public string Nit { get; init; } = string.Empty;

    /// <summary>Razón Social — <c>identity.tenants.legal_name</c>.</summary>
    public string RazonSocial { get; init; } = string.Empty;

    /// <summary>Estado activo — <c>identity.tenants.is_active</c>.</summary>
    public bool EstadoActivo { get; init; }

    /// <summary>Fecha de creación — <c>identity.tenants.created_at</c>.</summary>
    public DateTimeOffset FechaCreacion { get; init; }
}
