namespace Flit.Modules.Security.Domain.Auth;

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Invalid credentials.")
    {
    }
}

public sealed class AccountNotActiveException : Exception
{
    public AccountNotActiveException()
        : base("Account is not active.")
    {
    }
}

public sealed class AccountSuspendedException : Exception
{
    public AccountSuspendedException()
        : base("Account is temporarily suspended.")
    {
    }
}

/// <summary>
/// El usuario tuvo al menos una asignación de rol alguna vez, pero TODAS fueron desactivadas
/// (HU #10507 AC2). Distinto de un usuario que nunca tuvo rol asignado (AC3), caso en el que
/// el login debe proceder con normalidad.
/// </summary>
public sealed class AllRolesInactiveException : Exception
{
    public AllRolesInactiveException()
        : base("All roles assigned to the user are inactive.")
    {
    }
}

/// <summary>
/// El token de recuperación es inválido, ya fue usado o expiró. Mensaje genérico para
/// no revelar cuál de los casos ocurrió.
/// </summary>
public sealed class InvalidResetTokenException : Exception
{
    public InvalidResetTokenException()
        : base("The password reset token is invalid or has expired.")
    {
    }
}

/// <summary>
/// La nueva contraseña no cumple los requisitos mínimos. La validación de complejidad
/// completa corresponde a la HU #10171; aquí solo se exige una longitud mínima.
/// </summary>
public sealed class WeakPasswordException : Exception
{
    public WeakPasswordException()
        : base("The new password does not meet the minimum requirements.")
    {
    }
}

/// <summary>El administrador no tiene ámbito (tenant/permiso) sobre el usuario objetivo (HU #10170).</summary>
public sealed class AdminScopeException : Exception
{
    public AdminScopeException()
        : base("The administrator has no scope over the target user.")
    {
    }
}

/// <summary>El usuario objetivo de una acción administrativa no existe o no está activo.</summary>
public sealed class TargetUserNotFoundException : Exception
{
    public TargetUserNotFoundException()
        : base("The target user was not found.")
    {
    }
}

/// <summary>La contraseña actual aportada en un cambio voluntario no coincide (HU #10171).</summary>
public sealed class InvalidCurrentPasswordException : Exception
{
    public InvalidCurrentPasswordException()
        : base("The current password is incorrect.")
    {
    }
}

/// <summary>Ya existe una invitación pending para ese email en el tenant (HU #10175 AC2).</summary>
public sealed class InvitationAlreadyPendingException : Exception
{
    public InvitationAlreadyPendingException()
        : base("There is already a pending invitation for this email in the tenant.")
    {
    }
}

/// <summary>El rol especificado no existe o no pertenece al tenant (HU #10175).</summary>
public sealed class RoleNotFoundException : Exception
{
    public RoleNotFoundException()
        : base("The specified role was not found in the tenant.")
    {
    }
}

/// <summary>Token de invitación inexistente o invitación no pending (HU #10177 AC2).</summary>
public sealed class InvalidInvitationTokenException : Exception
{
    public InvalidInvitationTokenException()
        : base("The invitation token is invalid or has already been used.")
    {
    }
}

/// <summary>Ya existe un usuario activo con ese email en el sistema — no se puede reinvitar.</summary>
public sealed class UserAlreadyExistsException : Exception
{
    public UserAlreadyExistsException()
        : base("A user with this email already has an active account.")
    {
    }
}

/// <summary>
/// Invitación sin ningún rol seleccionado (HU #10506 AC5). Seleccionar al menos un rol al
/// invitar es OBLIGATORIO — ya no se permite "sin rol asignado" como en HU #10175.
/// </summary>
public sealed class NoRolesSelectedException : Exception
{
    public NoRolesSelectedException()
        : base("At least one role must be selected to invite a user.")
    {
    }
}

/// <summary>
/// La invitación no existe, o existe pero no pertenece al alcance (tenant) del caller
/// (HU #10625 reenvío, HU #10627 cancelación). Se usa el mismo error para ambos casos para no
/// revelar la existencia de invitaciones fuera de alcance — se mapea a 404 en el endpoint.
/// </summary>
public sealed class InvitationNotFoundException : Exception
{
    public InvitationNotFoundException()
        : base("The invitation was not found.")
    {
    }
}

/// <summary>
/// La invitación ya no está en estado "pending" — fue aceptada o cancelada previamente, por lo
/// que no se puede reenviar (HU #10625 AC3) ni cancelar (HU #10627 AC2). Es un error de negocio
/// explícito, no un no-op silencioso.
/// </summary>
public sealed class InvitationNotPendingException : Exception
{
    public InvitationNotPendingException()
        : base("The invitation is no longer pending (it was already accepted or cancelled).")
    {
    }
}

/// <summary>
/// Reenvío rechazado por el cooldown anti-abuso: la invitación ya se reenvió hace menos del
/// tiempo mínimo configurado (HU #10625 AC2, <c>InvitationOptions.ResendCooldown</c>).
/// </summary>
public sealed class ResendCooldownActiveException(TimeSpan retryAfter)
    : Exception("You must wait before resending this invitation again.")
{
    /// <summary>Tiempo restante hasta que se pueda reenviar de nuevo.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;
}
