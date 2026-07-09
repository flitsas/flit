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
public sealed class InvalidTargetEntityTypeException : Exception
{
    public InvalidTargetEntityTypeException()
        : base("TargetEntityType must be either COMPANY or TRANSIT_OFFICE.")
    {
    }
}
