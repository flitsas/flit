using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14694 (plan §1.1, decisión E2) — productos cuyo servicio se aprovisiona al encenderlos. Para ellos el PUT de
/// productos exige la precondición de activación (<see cref="IPrecondicionActivacion"/>) y deja el trabajo
/// <c>&lt;producto&gt;.producto.cambiado</c> en la outbox; además tienen vista previa del alta y «Reintentar».
/// Hoy solo <c>impuestos</c>.
/// </summary>
public static class ProductosConAprovisionamiento
{
    /// <summary>Los códigos del catálogo con aprovisionamiento.</summary>
    public static readonly IReadOnlySet<string> Codigos = new HashSet<string>(StringComparer.Ordinal) { ProductCodes.Impuestos };

    /// <summary>¿El producto tiene aprovisionamiento? Normaliza el código (trim y minúsculas).</summary>
    public static bool Tiene(string? productCode) =>
        !string.IsNullOrWhiteSpace(productCode) && Codigos.Contains(productCode.Trim().ToLowerInvariant());
}
