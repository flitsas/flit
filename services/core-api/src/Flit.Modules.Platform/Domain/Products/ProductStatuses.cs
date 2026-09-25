namespace Flit.Modules.Platform.Domain.Products;

/// <summary>
/// Estados de un producto del catálogo (<c>ck_products_status</c> del DDL 119). Describen si el
/// producto existe para la plataforma, no si una empresa lo tiene: eso es <c>tenant_products</c>.
/// </summary>
public static class ProductStatuses
{
    public const string Active = "active";

    public const string Inactive = "inactive";
}
