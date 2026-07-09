namespace Flit.Modules.Security.Domain.UserRoles;

public sealed class UserOutOfScopeException : Exception
{
    public UserOutOfScopeException()
        : base("The user does not belong to the caller's tenant.")
    {
    }
}

public sealed class RoleForAssignmentNotFoundException : Exception
{
    public RoleForAssignmentNotFoundException()
        : base("The role was not found or is not active in this tenant.")
    {
    }
}

public sealed class SelfRoleAssignmentException : Exception
{
    public SelfRoleAssignmentException()
        : base("A user cannot change their own role.")
    {
    }
}

/// <summary>
/// El usuario ya tiene una asignación ACTIVA de ese mismo rol en el tenant (HU #10506 AC2).
/// Modelo aditivo: a diferencia del rol único de HU #10164, un usuario puede tener varios
/// roles activos simultáneos, pero no el MISMO rol dos veces.
/// </summary>
public sealed class RoleAlreadyAssignedException : Exception
{
    public RoleAlreadyAssignedException()
        : base("The user already has this role actively assigned in the tenant.")
    {
    }
}

/// <summary>
/// El rol asignado no aplica al tipo de entidad del tenant destino (HU #10506): un rol
/// TRANSIT_OFFICE no puede asignarse en un tenant COMPANY y viceversa.
/// </summary>
public sealed class RoleTargetEntityTypeMismatchException : Exception
{
    public RoleTargetEntityTypeMismatchException()
        : base("The role's target entity type does not match the destination tenant's type.")
    {
    }
}

/// <summary>
/// No existe una asignación ACTIVA de ese rol puntual para el usuario en el tenant — nada que
/// quitar (HU #10506 AC3, <c>RemoveRoleAssignmentHandler</c>).
/// </summary>
public sealed class RoleAssignmentNotFoundException : Exception
{
    public RoleAssignmentNotFoundException()
        : base("No active assignment of this role was found for the user in the tenant.")
    {
    }
}
