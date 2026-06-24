namespace Flit.Modules.Security.Domain.Modules;

public sealed class ModuleCodeDuplicateException : Exception
{
    public ModuleCodeDuplicateException()
        : base("A module with the same code already exists.")
    {
    }
}

public sealed class ModuleNotFoundException : Exception
{
    public ModuleNotFoundException()
        : base("The module was not found.")
    {
    }
}

public sealed class ModuleHasActivePermissionsException : Exception
{
    public ModuleHasActivePermissionsException()
        : base("The module cannot be deleted because it has active permissions.")
    {
    }
}
