namespace Flit.Ict.Application.Auth.Login;

/// <summary>
/// Petición de login ICT. <paramref name="CompanyManagerId"/> se acepta por compatibilidad con
/// clientes v1 pero se IGNORA (el tenant sale de la credencial, no del payload).
/// </summary>
public sealed record LoginIntegrationClientCommand(string Username, string Password, long? CompanyManagerId);

/// <summary>
/// Resultado del login. Si <see cref="MustRotate"/> es true, no se emite token (el cliente debe rotar
/// la contraseña primero).
/// </summary>
public sealed record LoginIntegrationClientResult(
    string? Token,
    int ExpiresInSeconds,
    bool MustRotate);
