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
