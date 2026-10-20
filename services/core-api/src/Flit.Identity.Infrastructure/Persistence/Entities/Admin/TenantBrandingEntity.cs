namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Identidad de marca de una cabeza de red MARCA_BLANCA — <c>admin.tenant_brandings</c>
/// (HU #12412, Feature #12366, ADR-0060 D1). Una fila por cabeza (<c>tenant_id</c> UNIQUE): el
/// borrador (<see cref="Draft"/>) se edita libremente y publicar copia el borrador a
/// <see cref="Published"/> incrementando <see cref="PublishedVersion"/>. Solo la cabeza tiene fila: la
/// hija hereda por <c>parent_tenant_id</c> en lectura. El motor exige <c>tenant_type = MARCA_BLANCA</c>
/// (<c>tr_tenant_brandings_marca_blanca</c>). Tenant-scoped (RLS decorativo; el aislamiento real es el
/// <c>WHERE tenant_id</c> del repositorio, patrón <c>CompanyPersonalizedDocumentEntity</c>).
/// </summary>
public sealed class TenantBrandingEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// Borrador editable, JSON compacto (<c>schemaVersion</c>, <c>platformName</c>,
    /// <c>colors{primary,secondary,onPrimary}</c>, <c>logoId</c>). Puede estar incompleto; la forma la
    /// valida la aplicación (#12413).
    /// </summary>
    public string Draft { get; set; } = "{\"schemaVersion\":1}";

    /// <summary>Snapshot publicado (misma forma que <see cref="Draft"/>, completo). <c>null</c> = nunca publicada.</summary>
    public string? Published { get; set; }

    /// <summary>Se incrementa en cada publicación; 0 = nunca publicada.</summary>
    public int PublishedVersion { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public Guid? PublishedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Retiro lógico de la marca (AC6): la fila se conserva y deja de resolverse.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public long RowVersion { get; set; }
}
