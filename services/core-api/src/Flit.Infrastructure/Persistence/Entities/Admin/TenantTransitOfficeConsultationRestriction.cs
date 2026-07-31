namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Restricción de consulta (RNMC, comparendos) que una compañía inhabilita para un
/// Organismo de Tránsito puntual —
/// <c>admin.tenant_transit_office_consultation_restrictions</c> (HU #10759).
/// Tabla dispersa: solo hay filas para los pares que el admin tocó explícitamente; ausencia
/// de fila = permisivo. RLS por <c>app.current_tenant_id</c>. Unicidad por
/// <c>(tenant_id, transit_office_id, consultation_kind)</c>.
/// </summary>
public sealed class TenantTransitOfficeConsultationRestriction
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    /// <summary>
    /// <c>rnmc</c> | <c>fines</c> (ver <c>RestrictedConsultationKinds</c> en Admin.Domain).
    /// CHECK cerrado en BD; "vehicle" queda deliberadamente fuera.
    /// </summary>
    public string ConsultationKind { get; set; } = string.Empty;

    /// <summary>Estado deseado: <c>true</c> permitida (default), <c>false</c> inhabilitada.</summary>
    public bool Enabled { get; set; } = true;

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
