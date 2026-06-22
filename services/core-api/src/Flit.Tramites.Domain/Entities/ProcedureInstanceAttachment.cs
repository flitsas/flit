namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Documento adjunto a una instancia de trámite (almacenamiento en disco local; deuda prod multi-instancia).
/// Slice 1 — schema núcleo del rework de trámites.
/// </summary>
public sealed class ProcedureInstanceAttachment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string Mimetype { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string Source { get; set; } = "user";
    public DateTimeOffset UploadedAt { get; set; }
    public Guid? UploadedBy { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
