namespace Flit.Modules.Security.Domain.UserManagement;

/// <summary>
/// El correo solicitado pertenece a una cuenta soft-deleted (HU #10621 AC3). Distinto de
/// <see cref="Auth.UserAlreadyExistsException"/> (cuenta ACTIVA): permite devolver un mensaje
/// específico sin exponer el error crudo de unicidad de BD (<c>uq_users_email</c> no es un
/// índice parcial, así que el correo sigue "ocupado" aunque la cuenta esté eliminada).
/// </summary>
public sealed class UserEmailBelongsToDeletedAccountException : Exception
{
    public UserEmailBelongsToDeletedAccountException()
        : base("Ese correo pertenece a una cuenta eliminada. Contacta a un SuperAdmin para restaurarla.")
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
