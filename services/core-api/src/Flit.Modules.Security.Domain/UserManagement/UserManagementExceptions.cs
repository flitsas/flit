namespace Flit.Modules.Security.Domain.UserManagement;

/// <summary>Un usuario no puede suspenderse/desactivarse a sí mismo (HU #10619 AC4).</summary>
public sealed class SelfSuspensionException : Exception
{
    public SelfSuspensionException()
        : base("A user cannot suspend or deactivate themselves.")
    {
    }
}

/// <summary>
/// La acción dejaría al tenant (o al sistema, para <c>SuperAdmin</c>) sin ningún administrador
/// activo (HU #10619 AC4): se rechaza la suspensión/desactivación del último admin disponible.
/// </summary>
public sealed class LastActiveAdminException : Exception
{
    public LastActiveAdminException()
        : base("This action would leave the tenant or the system without any active administrator.")
    {
    }
}

/// <summary>El usuario no tiene ninguna suspensión activa para levantar (HU #10619).</summary>
public sealed class NoActiveSuspensionException : Exception
{
    public NoActiveSuspensionException()
        : base("The user does not have an active suspension.")
    {
    }
}
