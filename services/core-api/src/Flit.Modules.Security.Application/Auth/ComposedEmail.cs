namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Asunto y cuerpo HTML de un correo, ya compuestos y listos para envolver en un
/// <see cref="Flit.Modules.Security.Domain.Auth.EmailMessage"/>.
/// </summary>
/// <remarks>
/// HU #11351 — tipo de retorno común de las funciones de composición pura de las plantillas
/// de Seguridad (invitación, recuperación de contraseña y reset administrativo). Cada función
/// de composición depende ÚNICAMENTE de sus argumentos: sin E/S, sin reloj, sin aleatoriedad,
/// sin estado. La generación de datos (tokens, contraseñas temporales) ocurre ANTES, en el
/// handler, y se pasa aquí como argumento — nunca al revés.
/// </remarks>
public sealed record ComposedEmail(string Subject, string HtmlBody);
