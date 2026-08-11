using Flit.Modules.Security.Application.Auth;

namespace Flit.Modules.Security.Application.Auth.AdminResetPassword;

/// <summary>
/// Plantilla del correo de reset administrativo de contraseña (HU #10170), extraída de
/// <see cref="AdminResetPasswordHandler"/> como función de composición pura (HU #11351).
/// </summary>
/// <remarks>
/// Riesgo señalado en el refinamiento: esta clase NUNCA genera la contraseña temporal — la
/// recibe como argumento ya generada por <c>ITemporaryPasswordGenerator</c> en el handler. Si la
/// generación se arrastrara aquí dentro, un fallo de composición dejaría al usuario con el hash
/// ya actualizado y sin acceso.
/// </remarks>
public static class AdminResetPasswordEmailTemplate
{
    public const string Subject = "Tu contraseña fue restablecida — FLIT";

    /// <summary>
    /// HU #11351 — composición pura: asunto y cuerpo a partir únicamente de
    /// <paramref name="displayName"/> y <paramref name="temporaryPassword"/> (ya generada por el
    /// llamante). Sin E/S, sin reloj, sin aleatoriedad, sin estado — la misma entrada produce
    /// siempre la misma salida.
    /// </summary>
    public static ComposedEmail Compose(string displayName, string temporaryPassword) =>
        new(Subject, BuildBody(displayName, temporaryPassword));

    private static string BuildBody(string displayName, string temporaryPassword)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "usuario" : displayName;
        return $"""
            <p>Hola {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>Un administrador restableció tu contraseña en FLIT. Tu contraseña temporal es:</p>
            <p><strong>{System.Net.WebUtility.HtmlEncode(temporaryPassword)}</strong></p>
            <p>Por seguridad, deberás definir una nueva contraseña la próxima vez que inicies sesión.</p>
            <p>— Equipo FLIT</p>
            """;
    }
}
