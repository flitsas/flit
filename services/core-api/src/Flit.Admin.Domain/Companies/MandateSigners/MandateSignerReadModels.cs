namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// Read model de un mandatario (firmante de mandato) y sus compañías asignadas dentro de un
/// organismo de tránsito (ADR-0023). El <c>DocumentNumber</c> es PII (Ley 1581): se entrega
/// solo en respuestas autenticadas de gestión y nunca debe registrarse en logs ni aparecer
/// en mensajes de error.
/// </summary>
public sealed class MandateSignerItem
{
    public Guid Id { get; init; }
    public Guid TransitOfficeId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    public string IntegrityHash { get; init; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Compañías (tenants gestores) actualmente asignadas al mandatario.</summary>
    public IReadOnlyList<Guid> CompanyTenantIds { get; init; } = [];
}

/// <summary>
/// Compañía gestora con grant en el organismo de tránsito (candidata a mandatario). Insumo
/// del multiselect del formulario (RF25 UI) y de la regla de uso RF33.
/// </summary>
public sealed class OtCompanyOption
{
    public Guid CompanyTenantId { get; init; }
    public string LegalName { get; init; } = string.Empty;

    /// <summary>La compañía (tenant) está activa — <c>identity.tenants.is_active</c>.</summary>
    public bool IsActive { get; init; }

    /// <summary>El grant compañía↔OT está habilitado — <c>admin.tenant_transit_office_grants.is_enabled</c>.</summary>
    public bool IsEnabled { get; init; }
}

/// <summary>
/// Resolución del mandatario activo configurado para una compañía dentro del OT (RF34). Se
/// consume en la vista consolidada de compañías por OT.
/// </summary>
public sealed class MandateSignerCompanyResolution
{
    public Guid CompanyTenantId { get; init; }
    public Guid MandateSignerId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string IntegrityHash { get; init; } = string.Empty;
}
