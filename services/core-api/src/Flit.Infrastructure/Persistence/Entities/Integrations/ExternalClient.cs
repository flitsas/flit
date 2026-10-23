namespace Flit.Infrastructure.Persistence.Entities.Integrations;

/// <summary>
/// HU #13084 — cliente de integración externo (<c>integrations.external_clients</c>, DDL 125).
/// Entidad de plataforma sin <c>tenant_id</c>: el cliente no pertenece a una compañía.
/// </summary>
public sealed class ExternalClient
{
    public Guid Id { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Finalidad del tratamiento de datos personales (Ley 1581).</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Hash Argon2id del secreto. @pii:high</summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>Hash del secreto anterior durante la ventana de rotación. @pii:high</summary>
    public string? PreviousSecretHash { get; set; }

    public DateTimeOffset? SecretRotatedAt { get; set; }

    public bool MustRotate { get; set; }

    /// <summary>Permisos como arreglo JSON.</summary>
    public string Scopes { get; set; } = "[]";

    public bool IsActive { get; set; } = true;

    public int FailedAttempts { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastTokenAt { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
