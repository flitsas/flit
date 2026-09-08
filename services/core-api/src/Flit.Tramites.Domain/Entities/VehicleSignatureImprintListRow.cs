namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Proyección de impronta firmada para listados OT — sin <see cref="VehicleSignatureImprint.PrivateKey"/>.
/// </summary>
public sealed class VehicleSignatureImprintListRow
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProcedureInstanceId { get; init; }
    public string Placa { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public Guid? AttachmentId { get; init; }
    public string PublicKey { get; init; } = string.Empty;
    public string DocumentHash { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public DateTimeOffset SignedAt { get; init; }
    public bool WasSignedWithoutOwnerSignature { get; init; }
    public string? SignedStoragePath { get; init; }
    public string? SignedSha256 { get; init; }
    public long? SignedSizeBytes { get; init; }
    public string? SignedFilename { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}
