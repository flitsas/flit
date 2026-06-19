using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.RememberUsername;

/// <summary>
/// "Recordar usuario" (HU #10203, RF20). Si el número de documento corresponde a una
/// cuenta activa, envía al correo registrado un recordatorio del usuario (email). En
/// cualquier otro caso no hace nada: el endpoint responde 202 genérico para no revelar
/// si el identificador existe (anti-enumeración / Habeas Data).
/// </summary>
public sealed class RememberUsernameHandler(
    IUserAccountRepository userAccountRepository,
    IEmailSender emailSender)
{
    public async Task HandleAsync(RememberUsernameCommand command, CancellationToken cancellationToken)
    {
        var document = command.DocumentNumber?.Trim() ?? string.Empty;
        if (document.Length == 0)
            return;

        var user = await userAccountRepository.FindActiveByDocumentAsync(document, cancellationToken);
        if (user is null)
            return;

        var message = new EmailMessage(
            user.Email,
            user.DisplayName,
            "Tu usuario registrado en FLIT",
            BuildBody(user.DisplayName, user.Email));

        await emailSender.SendAsync(message, cancellationToken);
    }

    private static string BuildBody(string displayName, string email)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "usuario" : displayName;
        return $"""
            <p>Hola {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>Tu usuario registrado para iniciar sesión en FLIT es:</p>
            <p><strong>{System.Net.WebUtility.HtmlEncode(email)}</strong></p>
            <p>Si no solicitaste este recordatorio, puedes ignorar este mensaje.</p>
            <p>— Equipo FLIT</p>
            """;
    }
}
