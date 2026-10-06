namespace Flit.Infrastructure.Persistence.Entities.Platform;

/// <summary>
/// Producto de la suite — <c>platform.products</c> (HU #12958, ADR-0063). Catálogo global sembrado
/// por el DDL 119: sin <c>tenant_id</c> ni RLS.
/// </summary>
public sealed class ProductEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre de ícono lucide.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary><c>active</c> | <c>inactive</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Orden en el menú de productos del hub.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
