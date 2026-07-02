namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Read model de un tenant Organismo de Tránsito (OT) para el listado/alta
/// administrativo. Proyección conjunta de <c>identity.tenants</c> (el tenant) y
/// <c>admin.transit_office_profiles</c> (el vínculo con la oficina del catálogo),
/// análoga a <see cref="CompanyListItem"/> para compañías B2B.
/// </summary>
public sealed class TransitOfficeTenantItem
{
    /// <summary>Id del tenant — <c>identity.tenants.id</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>Razón social — <c>identity.tenants.legal_name</c>.</summary>
    public string LegalName { get; init; } = string.Empty;

    /// <summary>NIT — <c>identity.tenants.tax_id</c>.</summary>
    public string TaxId { get; init; } = string.Empty;

    /// <summary>Código único del tenant — <c>identity.tenants.code</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Tipo de tenant — siempre <c>RENTING</c> para OT (catálogo B2B restringido por
    /// la migración <c>RestrictTenantTypeCatalog</c>; ver <see cref="Create.NewTransitOffice"/>).
    /// </summary>
    public string TenantType { get; init; } = string.Empty;

    /// <summary>Estado activo — <c>identity.tenants.is_active</c>.</summary>
    public bool EstadoActivo { get; init; }

    /// <summary>Fecha de creación del tenant — <c>identity.tenants.created_at</c>.</summary>
    public DateTimeOffset FechaCreacion { get; init; }

    /// <summary>Token de concurrencia optimista del tenant — <c>identity.tenants.row_version</c>.</summary>
    public long RowVersion { get; init; }

    /// <summary>Oficina del catálogo vinculada — <c>admin.transit_office_profiles.transit_office_id</c>.</summary>
    public Guid TransitOfficeId { get; init; }

    /// <summary>Nombre de la oficina del catálogo (para mostrar en listados).</summary>
    public string TransitOfficeName { get; init; } = string.Empty;

    /// <summary>Código de la oficina del catálogo.</summary>
    public string TransitOfficeCode { get; init; } = string.Empty;

    /// <summary>Modo de operación del perfil OT — <c>admin.transit_office_profiles.operation_mode</c>.</summary>
    public string OperationMode { get; init; } = string.Empty;
}
