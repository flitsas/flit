namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Read model del estado operativo de un organismo de tránsito para el listado
/// administrativo del SuperAdmin (RF01/RF02/RF03). Proyección de
/// <c>catalogs.transit_offices</c> LEFT JOIN <c>admin.transit_office_profiles</c> +
/// <c>identity.tenants</c>: una fila por oficina del catálogo, con o sin tenant OT
/// asociado.
/// </summary>
public sealed class TransitOfficeOperationalStatusItem
{
    /// <summary>Id de la oficina del catálogo — <c>catalogs.transit_offices.id</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>Código de la oficina — <c>catalogs.transit_offices.code</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Nombre de la oficina — <c>catalogs.transit_offices.name</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Código de departamento — <c>catalogs.transit_offices.department_code</c>.</summary>
    public string DepartmentCode { get; init; } = string.Empty;

    /// <summary>Indica si la oficina ya tiene un tenant OT dado de alta (perfil OT).</summary>
    public bool HasTenant { get; init; }

    /// <summary>Id del tenant OT asociado — <c>identity.tenants.id</c>, o <c>null</c> si no hay alta.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Estado activo del tenant OT — <c>identity.tenants.is_active</c>. <c>null</c> cuando la
    /// oficina aún no tiene tenant (sin alta).
    /// </summary>
    public bool? EstadoActivo { get; init; }

    /// <summary>
    /// Modo de operación del perfil OT — <c>admin.transit_office_profiles.operation_mode</c>.
    /// <c>null</c> cuando la oficina aún no tiene tenant.
    /// </summary>
    public string? OperationMode { get; init; }
}
