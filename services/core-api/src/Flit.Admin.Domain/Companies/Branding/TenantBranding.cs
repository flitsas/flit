namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Identidad de marca de una cabeza MARCA_BLANCA (HU #12412, ADR-0060 D1) — proyección de
/// <c>admin.tenant_brandings</c>. Una fila por cabeza (<c>tenant_id</c> UNIQUE). El borrador se edita
/// libremente; publicar copia <see cref="Draft"/> a <see cref="Published"/> incrementando
/// <see cref="PublishedVersion"/>. <see cref="DeletedAt"/> es el retiro lógico (AC6): la fila se
/// conserva y deja de resolverse, pero sigue siendo legible/editable por quien gobierna la marca.
/// </summary>
public sealed class TenantBranding
{
    public required Guid TenantId { get; init; }

    public required BrandingDraft Draft { get; init; }

    public BrandingDraft? Published { get; init; }

    public int PublishedVersion { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public Guid? PublishedBy { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }

    public required long RowVersion { get; init; }

    /// <summary>El borrador difiere de lo publicado (o nunca se publicó nada).</summary>
    public bool HasUnpublishedChanges => Published is null || Draft != Published;

    public bool IsRetired => DeletedAt is not null;
}
