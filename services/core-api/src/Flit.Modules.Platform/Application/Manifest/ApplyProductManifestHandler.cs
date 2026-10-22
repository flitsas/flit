using Flit.Modules.Platform.Domain.Manifest;
using Flit.Modules.Platform.Domain.Products;

namespace Flit.Modules.Platform.Application.Manifest;

/// <summary>
/// Valida y aplica el manifiesto de un producto (contrato v1 §6, HU #12966). Idempotente: repetirlo no
/// crea nada nuevo.
/// </summary>
/// <remarks>
/// Reglas del contrato: cada <c>slug</c> empieza por <c>&lt;code&gt;.</c> (un producto no puede registrar
/// permisos de otro), y los roles por defecto solo usan permisos del mismo manifiesto.
/// </remarks>
public sealed class ApplyProductManifestHandler
{
    private readonly IProductCatalog _catalog;
    private readonly IProductManifestStore _store;

    public ApplyProductManifestHandler(IProductCatalog catalog, IProductManifestStore store)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ManifestApplyResult> HandleAsync(ProductManifest manifest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var product = await _catalog.FindAsync(manifest.ProductCode, ct).ConfigureAwait(false)
            ?? throw new ManifestValidationException("PRODUCT_NOT_FOUND", $"El producto '{manifest.ProductCode}' no existe.");
        if (!product.IsActive)
            throw new ManifestValidationException("PRODUCT_INACTIVE", $"El producto '{manifest.ProductCode}' está inactivo.");

        var prefix = manifest.ProductCode + ".";
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in manifest.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Code) || string.IsNullOrWhiteSpace(module.Name))
                throw new ManifestValidationException("MANIFEST_INVALID", "Cada módulo necesita code y name.");

            foreach (var permission in module.Permissions)
            {
                if (!permission.Slug.StartsWith(prefix, StringComparison.Ordinal) || permission.Slug.Length == prefix.Length)
                    throw new ManifestValidationException("MANIFEST_SLUG_PREFIX", $"El permiso '{permission.Slug}' debe empezar por '{prefix}'.");
                if (!slugs.Add(permission.Slug))
                    throw new ManifestValidationException("MANIFEST_INVALID", $"El permiso '{permission.Slug}' está repetido.");
            }
        }

        foreach (var role in manifest.DefaultRoles)
        {
            if (string.IsNullOrWhiteSpace(role.Code) || string.IsNullOrWhiteSpace(role.Name))
                throw new ManifestValidationException("MANIFEST_INVALID", "Cada rol necesita code y name.");
            var unknown = role.Permissions.FirstOrDefault(s => !slugs.Contains(s));
            if (unknown is not null)
                throw new ManifestValidationException("MANIFEST_INVALID", $"El rol '{role.Code}' usa '{unknown}', que no está en el manifiesto.");
        }

        return await _store.ApplyAsync(manifest, ct).ConfigureAwait(false);
    }
}

/// <summary>Manifiesto inválido: <see cref="Code"/> se devuelve tal cual en el 400.</summary>
public sealed class ManifestValidationException : Exception
{
    public ManifestValidationException()
    {
        Code = string.Empty;
    }

    public ManifestValidationException(string message)
        : base(message)
    {
        Code = string.Empty;
    }

    public ManifestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = string.Empty;
    }

    public ManifestValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
