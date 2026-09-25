namespace Flit.Modules.Platform.Domain.Manifest;

/// <summary>
/// Manifiesto de un producto (contrato v1 §6, <c>PUT /products/{code}/manifest</c>): sus módulos, permisos
/// y roles por defecto. Cada servicio lo registra al arrancar, de forma idempotente (ADR-0063 §5).
/// </summary>
public sealed record ProductManifest(
    string ProductCode,
    string Version,
    IReadOnlyList<ManifestModule> Modules,
    IReadOnlyList<ManifestRole> DefaultRoles);

public sealed record ManifestModule(string Code, string Name, IReadOnlyList<ManifestPermissionSlug> Permissions);

public sealed record ManifestPermissionSlug(string Slug, string Name);

/// <summary>Rol por defecto del producto. <see cref="Permissions"/> son slugs del mismo manifiesto.</summary>
public sealed record ManifestRole(string Code, string Name, IReadOnlyList<string> Permissions);

/// <summary>Resultado de aplicar un manifiesto: cuánto se creó.</summary>
public sealed record ManifestApplyResult(int ModulesCreated, int PermissionsCreated, int RolesCreated);

/// <summary>Persistencia del manifiesto en el RBAC (<c>security.modules</c>, <c>permissions</c>, <c>roles</c>).</summary>
public interface IProductManifestStore
{
    /// <summary>
    /// Crea o actualiza, en una transacción, los módulos (por <c>code</c>), permisos (por <c>slug</c>) y roles
    /// por defecto (por <c>code</c> en <c>COMPANY</c>) del manifiesto, todos del producto del manifiesto.
    /// No borra nada: un permiso o un grant que ya no esté en el manifiesto se conserva.
    /// </summary>
    /// <exception cref="ManifestConflictException">Un módulo, permiso o rol del manifiesto ya existe en otro producto.</exception>
    Task<ManifestApplyResult> ApplyAsync(ProductManifest manifest, CancellationToken ct);
}

/// <summary>Un código del manifiesto ya pertenece a otro producto.</summary>
public sealed class ManifestConflictException : Exception
{
    public ManifestConflictException()
    {
    }

    public ManifestConflictException(string message)
        : base(message)
    {
    }

    public ManifestConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
