namespace Flit.Admin.Application.Integrations.Auth;

/// <summary>HU #13087 — petición de pase: identificador y secreto del cliente (contrato v3.1 §2).</summary>
public sealed record IssueExternalClientTokenCommand(string? ClientId, string? ClientSecret);

/// <summary>Resultado de la petición de pase.</summary>
public enum ExternalTokenStatus
{
    Issued,

    /// <summary>Credenciales inválidas, cliente inexistente o inactivo: no se revela cuál (401).</summary>
    InvalidClient,

    /// <summary>Bloqueado por intentos fallidos (423 con <c>Retry-After</c>).</summary>
    Locked,

    /// <summary>Rotación obligatoria pendiente (403).</summary>
    RotationRequired,

    /// <summary>Sin llave del emisor configurada (503).</summary>
    Unavailable,
}

public sealed record IssueExternalClientTokenResult(
    ExternalTokenStatus Status,
    string? AccessToken = null,
    int ExpiresInSeconds = 0,
    IReadOnlyList<string>? Scopes = null,
    TimeSpan? RetryAfter = null);

/// <summary>
/// Parámetros del bloqueo y de la rotación (sección <c>ExternalClients</c>). Los mismos 5 intentos y
/// 15 minutos que ICT; la ventana de gracia del secreto anterior es de 24 h (plan de la épica §3.3).
/// </summary>
public sealed class ExternalClientAuthSettings
{
    public const string SectionName = "ExternalClients";

    public int MaxFailedAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;

    public int SecretGraceHours { get; init; } = 24;
}
