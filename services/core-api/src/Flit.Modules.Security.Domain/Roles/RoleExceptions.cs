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
