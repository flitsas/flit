namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Política de bloqueo de preflight que una compañía fija para un Organismo de Tránsito puntual —
/// <c>admin.tenant_transit_office_blocking_policies</c> (FEATURE 05). Por cada criterio
/// (SOAT, RTM, estado del vehículo, comparendos, RNMC) decide si un hallazgo negativo BLOQUEA
/// (rojo, subsanable) o solo ADVIERTE (amarillo, el usuario decide continuar).
/// Tabla dispersa: solo hay filas para los pares que el admin tocó explícitamente; ausencia de
/// fila = default del criterio. RLS por <c>app.current_tenant_id</c>. Unicidad por
/// <c>(tenant_id, transit_office_id, criterion)</c>.
/// </summary>
public sealed class TenantTransitOfficeBlockingPolicy
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    /// <summary>
    /// <c>soat</c> | <c>rtm</c> | <c>estado_vehiculo</c> | <c>fines</c> | <c>rnmc</c>
    /// (ver <c>BlockingCriteria</c> en Admin.Domain). CHECK cerrado en BD.
    /// </summary>
    public string Criterion { get; set; } = string.Empty;

    /// <summary>Estado deseado: <c>true</c> bloquea (fail→rojo), <c>false</c> solo advierte (warn).</summary>
    public bool Blocks { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
