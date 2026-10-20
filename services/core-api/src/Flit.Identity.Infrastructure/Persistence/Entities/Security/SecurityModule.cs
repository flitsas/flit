using System.ComponentModel.DataAnnotations.Schema;
using Flit.Infrastructure.Persistence.Entities.Common;

namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class SecurityModule : AuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public short SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Producto al que pertenece el módulo (<c>platform.products.code</c>, HU #12964 / ADR-0063):
    /// <c>plataforma</c> o <c>tramites</c>. Un rol solo puede tener permisos de módulos de su producto.
    /// </summary>
    public string ProductCode { get; set; } = "tramites";

    [NotMapped]
    public int PermissionCount { get; set; }
}
