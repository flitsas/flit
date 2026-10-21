using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Security.Application.UserRoles;

/// <summary>
/// Producto de un rol para decidir la purga de permisos individuales (HU #13646). <c>security.roles.product_code</c> es NOT NULL con
/// default <c>tramites</c>; un valor nulo o vacío (snapshot sin producto, datos previos) se trata como ese producto por defecto,
/// coherente con <c>Role.ProductCode</c>, para que el cambio o la baja de un rol nunca deje overrides vivos "en silencio".
/// </summary>
internal static class RoleProductCode
{
    public static string Normalize(string? productCode) =>
        string.IsNullOrWhiteSpace(productCode) ? ProductCodes.Tramites : productCode;
}
