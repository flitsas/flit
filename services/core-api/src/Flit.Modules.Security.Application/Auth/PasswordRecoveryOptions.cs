namespace Flit.Modules.Security.Application.Auth;

/// <summary>Parámetros del flujo de recuperación de contraseña (sección "PasswordRecovery").</summary>
public sealed class PasswordRecoveryOptions
{
    public const string SectionName = "PasswordRecovery";

    /// <summary>Base del enlace de restablecimiento (frontend). El token se añade como query "token".</summary>
    public string ResetUrlBase { get; set; } = "http://localhost:4001/reset-password";

    /// <summary>Vigencia del token de recuperación en minutos.</summary>
    public int TokenLifetimeMinutes { get; set; } = 30;

    /// <summary>Longitud mínima de la nueva contraseña (la complejidad completa es HU #10171).</summary>
    public int MinPasswordLength { get; set; } = 8;
}
