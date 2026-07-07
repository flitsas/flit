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
