namespace Flit.Modules.Security.Domain.Permissions;

public sealed class PermissionSlugDuplicateException : Exception
{
    public PermissionSlugDuplicateException()
        : base("A permission with the same slug already exists.")
    {
    }
}

public sealed class PermissionNotFoundException : Exception
{
    public PermissionNotFoundException()
        : base("The permission was not found.")
    {
    }
}

public sealed class ModuleInactiveException : Exception
{
    public ModuleInactiveException()
        : base("The module does not exist or is inactive.")
    {
    }
}

public sealed class PermissionInUseException : Exception
{
    public PermissionInUseException()
        : base("The permission cannot be deleted because it is in use by one or more roles.")
    {
    }
}
