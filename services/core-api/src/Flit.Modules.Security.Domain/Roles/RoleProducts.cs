namespace Flit.Modules.Security.Domain.Roles;

/// <summary>
/// HU #13441 (Épica #12750), adaptación a staging: staging aún no tiene la ola multi-producto
/// (<c>platform.products</c>, <c>security.modules.product_code</c>, <c>platform.tenant_products</c>). El producto de un
/// permiso se deriva del código de su módulo con el mismo reparto que el DDL 120 de develop: <c>auth</c>,
/// <c>usuarios</c>, <c>rbac</c> y <c>banners</c> son de <c>plataforma</c> (no delegable); el resto, de <c>tramites</c>.
/// Al llevar develop a staging, esta clase se retira en favor de <c>ProductCodes</c> y la columna real.
/// </summary>
public static class RoleProducts
{
    public const string Plataforma = "plataforma";

    public const string Tramites = "tramites";

    /// <summary>Productos conocidos (los mismos de develop); <c>admin_&lt;producto&gt;</c> queda reservado.</summary>
    public static IReadOnlyList<string> All { get; } = [Plataforma, Tramites, "comparendos", "diagnostico"];

    /// <summary>Productos encendidos en staging: no hay habilitación por tenant (HU #10664), todos tienen Trámites.</summary>
    public static IReadOnlySet<string> Enabled { get; } = new HashSet<string>(StringComparer.Ordinal) { Plataforma, Tramites };

    /// <summary>Módulos del producto <c>plataforma</c> (DDL 120 de develop).</summary>
    public static readonly string[] PlatformModuleCodes = ["auth", "usuarios", "rbac", "banners"];

    public static string OfModule(string moduleCode) =>
        PlatformModuleCodes.Contains(moduleCode, StringComparer.Ordinal) ? Plataforma : Tramites;
}
