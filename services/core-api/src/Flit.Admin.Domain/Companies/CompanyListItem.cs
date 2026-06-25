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

    /// <summary>Código único del tenant — <c>identity.tenants.code</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Tipo de compañía — <c>identity.tenants.tenant_type</c>.</summary>
    public string TenantType { get; init; } = string.Empty;

    /// <summary>Estado activo — <c>identity.tenants.is_active</c>.</summary>
    public bool EstadoActivo { get; init; }

    /// <summary>Fecha de creación — <c>identity.tenants.created_at</c>.</summary>
    public DateTimeOffset FechaCreacion { get; init; }

    /// <summary>
    /// Token de concurrencia optimista — <c>identity.tenants.row_version</c> (lo incrementa
    /// el trigger <c>trg_row_version</c> en cada UPDATE). El cliente lo reenvía al editar
    /// para detectar ediciones concurrentes (lost update) → 409 Conflict.
    /// </summary>
    public long RowVersion { get; init; }
}
