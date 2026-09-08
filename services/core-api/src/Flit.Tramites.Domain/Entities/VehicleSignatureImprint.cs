namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Auditoría de firma digital de una impronta manual (paridad legacy
/// <c>vehicle_signature_imprints</c>). Se crea al estampar en consolidado OT.
/// Al reemplazar el adjunto: soft-delete + <see cref="AttachmentId"/> a null;
/// el snapshot <c>signed_*</c> conserva path/sha del PDF firmado.
/// </summary>
public sealed class VehicleSignatureImprint
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Adjunto vigente al firmar; null tras reemplazo/borrado.</summary>
    public Guid? AttachmentId { get; set; }

    /// <summary>Paridad legacy <c>id_module</c> (p. ej. tipología o <c>tramites</c>).</summary>
    public string ModuleCode { get; set; } = "tramites";

    /// <summary>PEM RSA privado efímero. PII/secreto: no loguear.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>PEM RSA público efímero.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>SHA-256 hex del PDF base (antes del stamp).</summary>
    public string DocumentHash { get; set; } = string.Empty;

    /// <summary>Firma RSA-SHA256 en Base64.</summary>
    public string Signature { get; set; } = string.Empty;

    public DateTimeOffset SignedAt { get; set; }

    public bool WasSignedWithoutOwnerSignature { get; set; }

    /// <summary>Path en storage del PDF ya firmado (snapshot de auditoría).</summary>
    public string? SignedStoragePath { get; set; }

    /// <summary>SHA-256 del PDF firmado (distinto de <see cref="DocumentHash"/>).</summary>
    public string? SignedSha256 { get; set; }

    public long? SignedSizeBytes { get; set; }

    public string? SignedFilename { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
