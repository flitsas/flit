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
/// Conflicto de concurrencia optimista al editar el perfil de un usuario (HU #10621 AC4): otro
/// administrador ya modificó la fila entre la carga del formulario y este submit. El
/// repositorio de infraestructura la traduce desde <c>DbUpdateConcurrencyException</c> (EF
/// Core) para que Domain/Application no dependan de EF.
/// </summary>
public sealed class UserProfileConcurrencyException : Exception
{
    public UserProfileConcurrencyException()
        : base("El usuario fue modificado por otro administrador. Recarga la información e inténtalo de nuevo.")
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

/// <summary>Un usuario no puede eliminarse a sí mismo (HU #10623 AC2).</summary>
public sealed class SelfDeletionException : Exception
{
    public SelfDeletionException()
        : base("A user cannot delete themselves.")
    {
    }
}

/// <summary>
/// Bug #13194 — no se puede restaurar una cuenta eliminada si su correo ya lo usa otra cuenta VIVA
/// (p. ej. se volvió a invitar y activar). <c>uq_users_email</c> es parcial (<c>deleted_at IS NULL</c>):
/// restaurar dejaría dos cuentas vivas con el mismo correo y la BD lo rechaza. El handler lo detecta
/// antes y el repositorio traduce el 23505 de la carrera a esta misma excepción.
/// </summary>
public sealed class UserEmailInUseByLiveAccountException : Exception
{
    public UserEmailInUseByLiveAccountException()
        : base("Ya existe una cuenta activa con ese correo.")
    {
    }
}

/// <summary>
/// El usuario objetivo de una restauración NO está eliminado (HU #10623 AC5): se rechaza
/// explícitamente en vez de tratar la restauración como un no-op silencioso.
/// </summary>
public sealed class UserNotDeletedException : Exception
{
    public UserNotDeletedException()
        : base("The user is not deleted, there is nothing to restore.")
    {
    }
}
