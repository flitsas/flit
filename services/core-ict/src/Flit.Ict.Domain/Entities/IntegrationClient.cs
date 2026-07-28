namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Credencial de un cliente de integración (login ICT independiente del login de plataforma).
/// Tabla <c>ict.integration_clients</c>. NO lleva RLS: el login busca por username sin conocer
/// aún el tenant; el <see cref="TenantId"/> se deriva de la fila y a partir de ahí gobierna el scope.
/// </summary>
public sealed class IntegrationClient : AuditableEntity
{
    public Guid TenantId { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>Hash Argon2id. PII sensible (@pii:high).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string? PreviousPasswordHash { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public bool MustRotate { get; set; }

    /// <summary>Scopes en JSON (p.ej. ["ict.transactions.write","ict.status.read"]).</summary>
    public string Scopes { get; set; } = "[]";

    public bool IsActive { get; set; } = true;

    public short FailedLoginAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime? LastLoginAt { get; set; }
}
