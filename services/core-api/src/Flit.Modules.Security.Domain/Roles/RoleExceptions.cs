namespace Flit.Modules.Security.Domain.Roles;

public sealed class RoleCodeDuplicateException : Exception
{
    public RoleCodeDuplicateException()
        : base("A role with the same code already exists in this tenant.")
    {
    }
}

public sealed class RoleNotFoundException : Exception
{
    public RoleNotFoundException()
        : base("The role was not found.")
    {
    }
}

public sealed class RoleSystemLockedException : Exception
{
    public RoleSystemLockedException()
        : base("System roles cannot be deleted.")
    {
    }
}

public sealed class RoleHasActiveUsersException : Exception
{
    public RoleHasActiveUsersException()
        : base("The role cannot be deleted because it has active user assignments.")
    {
    }
}

/// <summary>
/// Fix post-review #10504: <c>TargetEntityType</c> solo admite <c>COMPANY</c> | <c>TRANSIT_OFFICE</c>
/// (mismo dominio que el <c>CHECK</c> constraint <c>ck_roles_target_entity_type</c> en Postgres).
/// Se valida en la capa de aplicación para devolver 400 en vez de dejar que la excepción cruda
/// del CHECK constraint burbujee como 500 sin manejar.
/// </summary>
/// <summary>
/// HU #12964 (contrato v1 §4): un rol solo puede tener permisos de módulos de su producto. SuperAdmin
/// queda exento por su bypass (§2.1).
/// </summary>
public sealed class RolePermissionProductMismatchException : Exception
{
    public RolePermissionProductMismatchException()
        : base("A role can only include permissions from modules of its own product.")
    {
    }
}

/// <summary>HU #12964: el producto del rol no existe en el contrato de plataforma (§1).</summary>
public sealed class InvalidRoleProductException : Exception
{
    public InvalidRoleProductException()
        : base("ProductCode must be one of the platform product codes.")
    {
    }
}

public sealed class InvalidTargetEntityTypeException : Exception
{
    public InvalidTargetEntityTypeException()
        : base("TargetEntityType must be either COMPANY or TRANSIT_OFFICE.")
    {
    }
}

/// <summary>
/// HU #13441: el rol es global (visible, de solo lectura) o de otro tenant, así que el Admin de Compañía no puede
/// cambiarlo ni eliminarlo. Se responde 403 <c>ROLE_READ_ONLY</c>; los roles de otro tenant ya no llegan aquí porque
/// el repositorio no los muestra (404 <c>RoleNotFoundException</c>).
/// </summary>
public sealed class RoleNotOwnedException : Exception
{
    public RoleNotOwnedException()
        : base("The role does not belong to the caller's tenant.")
    {
    }
}

/// <summary>Códigos de error del tope de privilegios (HU #13441 AC3).</summary>
public static class PrivilegeCeilingCodes
{
    public const string NotHeld = "PERMISSION_NOT_HELD";
    public const string ModuleNotEnabled = "PERMISSION_MODULE_NOT_ENABLED";
    public const string PlatformOnly = "PERMISSION_PLATFORM_ONLY";
    public const string Unknown = "PERMISSION_NOT_FOUND";
}

/// <summary>
/// HU #13441 AC3: el Admin de Compañía intentó otorgar un permiso que no posee, de un módulo no habilitado para su
/// tenant o de plataforma. <see cref="Code"/> es el código explícito que ve el cliente.
/// </summary>
public sealed class PrivilegeCeilingException(string code, IReadOnlyList<string> slugs)
    : Exception($"Cannot grant permissions ({code}): {string.Join(", ", slugs)}")
{
    public string Code { get; } = code;

    public IReadOnlyList<string> Slugs { get; } = slugs;
}

/// <summary>HU #13441: code o nombre del rol vacío o fuera de formato (code 2-50: letras, números, punto, guion, guion bajo).</summary>
public sealed class InvalidRoleInputException : Exception
{
    public InvalidRoleInputException()
        : base("Role code or name is invalid.")
    {
    }
}
