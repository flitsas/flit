using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.ForgotPassword;

namespace Flit.Infrastructure.Notifications.Preview;

/// <summary>
/// Muestra de las 3 plantillas de correo de Seguridad (invitación, recuperación de contraseña y
/// reset administrativo) para previsualización (HU #11354, Feature #11347).
/// </summary>
/// <remarks>
/// <para>
/// Sigue el mismo patrón que <c>Flit.Infrastructure.Documents.MandatoPreviewSample</c>
/// (Plataforma → Mandatos): cada campo variable se rellena con un MARCADOR visible en
/// mayúsculas entre corchetes — nunca con un nombre, documento, correo o teléfono inventado
/// (AC1, Ley 1581).
/// </para>
/// <para>
/// AC2 — el marcador de la contraseña temporal del reset administrativo es un texto fijo; esta
/// clase no conoce ni referencia <c>ITemporaryPasswordGenerator</c>, así que una muestra jamás
/// consume el generador de contraseñas reales.
/// </para>
/// <para>
/// AC3 — cada muestra se compone con la MISMA función <c>Compose</c> que usa producción
/// (<see cref="InvitationEmailTemplate.Compose"/>, <see cref="ForgotPasswordEmailTemplate.Compose"/>,
/// <see cref="AdminResetPasswordEmailTemplate.Compose"/>): esta clase solo decide qué
/// argumentos (marcadores) le pasa, nunca construye el HTML por su cuenta.
/// </para>
/// <para>
/// El enlace de activación/recuperación normalmente se arma con
/// <c>BuildActivateLink</c>/<c>BuildResetLink</c> a partir de una URL base y un token real; en
/// modo muestra el TOKEN también es un marcador (no un token generado ni real), así que el
/// enlace completo se compone a mano con el mismo formato de query string.
/// </para>
/// </remarks>
public static class SecurityEmailPreviewSample
{
    // Marcadores en mayúsculas entre corchetes — ningún dato de persona, real ni inventado.
    public const string PhNombreDestinatario = "[ACÁ VA EL NOMBRE DEL DESTINATARIO]";
    public const string PhToken = "[ACÁ VA EL TOKEN]";
    public const string PhContrasenaTemporal = "[ACÁ VA LA CONTRASEÑA TEMPORAL]";

    private const string ActivateUrlBase = "https://app.flit.test/invite/activate";
    private const string ResetUrlBase = "https://app.flit.test/password/reset";

    /// <summary>Muestra de <see cref="InvitationEmailTemplate"/> (plantilla <c>security.invitation</c>).</summary>
    public static ComposedEmail BuildInvitation()
    {
        var link = InvitationEmailTemplate.BuildActivateLink(ActivateUrlBase, PhToken);
        return InvitationEmailTemplate.Compose(PhNombreDestinatario, link);
    }

    /// <summary>Muestra de <see cref="ForgotPasswordEmailTemplate"/> (plantilla <c>security.forgot-password</c>).</summary>
    public static ComposedEmail BuildForgotPassword(int lifetimeMinutes = 30)
    {
        var link = ForgotPasswordEmailTemplate.BuildResetLink(ResetUrlBase, PhToken);
        return ForgotPasswordEmailTemplate.Compose(PhNombreDestinatario, link, lifetimeMinutes);
    }

    /// <summary>
    /// Muestra de <see cref="AdminResetPasswordEmailTemplate"/> (plantilla
    /// <c>security.admin-reset-password</c>). AC2: el marcador <see cref="PhContrasenaTemporal"/>
    /// es un literal — nunca se invoca <c>ITemporaryPasswordGenerator</c> para producirlo.
    /// </summary>
    public static ComposedEmail BuildAdminResetPassword() =>
        AdminResetPasswordEmailTemplate.Compose(PhNombreDestinatario, PhContrasenaTemporal);
}
