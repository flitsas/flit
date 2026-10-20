namespace Flit.Modules.Platform.Domain.Products;

/// <summary>
/// Producto de la FLIT Suite (<c>platform.products</c>, ADR-0063). Es lo que se enciende o se apaga
/// para una empresa; los permisos dentro de cada producto siguen siendo módulos (HU #10664).
/// </summary>
/// <param name="Code">Código del contrato de plataforma v1, §1 (<c>ProductCodes</c>).</param>
/// <param name="Name">Nombre visible en el hub y en <c>GET /me/apps</c>.</param>
/// <param name="Icon">Nombre del ícono (lucide) que pinta el hub.</param>
/// <param name="Status"><see cref="ProductStatuses.Active"/> o <see cref="ProductStatuses.Inactive"/>.</param>
public sealed record Product(string Code, string Name, string Icon, string Status)
{
    /// <summary>Un producto inactivo no se puede encender para ninguna empresa.</summary>
    public bool IsActive => Status == ProductStatuses.Active;
}
