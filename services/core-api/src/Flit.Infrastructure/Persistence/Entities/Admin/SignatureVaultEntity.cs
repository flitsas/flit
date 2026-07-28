namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Firma precargada del apoderado custodiada en el baúl — <c>admin.signature_vault</c>
/// (HU #10642, ADR-0025). Tabla tenant-scoped (RLS por <c>tenant_id</c>).
///
/// <c>DocumentNumber</c> es PII (Ley 1581): no debe registrarse en logs ni exponerse en
/// mensajes de error. El material criptográfico de la firma <b>nunca</b> se persiste aquí:
/// solo se referencia el artefacto en S3 vía <c>StoragePath</c> + <c>StorageSha256</c>
/// (ADR-0025 §3). <c>Estado</c> ('activa'|'revocada') es la baja lógica explícita y la
/// vigencia (<c>VigenciaDesde</c>/<c>VigenciaHasta</c>) se enforce en BD y dominio.
/// </summary>
public sealed class SignatureVaultEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    /// <summary>PII (Ley 1581): no loguear ni exponer.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>
    /// NIT de la compañía. DEPRECADO (HU #10930, Feature #10929): la firma es de la persona +
    /// tenant, el NIT deja de ser obligatorio y sale de la llave de unicidad; nullable durante la
    /// transición (sigue consultable).
    /// </summary>
    public string? NitEmpresa { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Huella de integridad SHA-256 de la firma (no material criptográfico).</summary>
    public string SignatureHash { get; set; } = string.Empty;

    /// <summary>
    /// Código alfanumérico que digita el usuario (HU #10930), DISTINTO de <see cref="SignatureHash"/>
    /// (que es el SHA-256 calculado del artefacto). Opcional.
    /// </summary>
    public string? CodigoHash { get; set; }

    /// <summary>Path del artefacto (PNG/PDF) en S3 vía <c>IAttachmentStorage</c>.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Integridad del artefacto almacenado.</summary>
    public string StorageSha256 { get; set; } = string.Empty;

    /// <summary>'activa' | 'revocada' (baja lógica explícita).</summary>
    public string Estado { get; set; } = "activa";

    public DateOnly VigenciaDesde { get; set; }

    public DateOnly VigenciaHasta { get; set; }

    /// <summary>FK opcional → <c>admin.mandate_signers</c> cuando el apoderado coincide con un firmante de mandato.</summary>
    public Guid? MandateSignerId { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
