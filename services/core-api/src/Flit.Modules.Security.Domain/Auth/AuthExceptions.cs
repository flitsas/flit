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
