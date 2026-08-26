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

    /// <summary>
    /// HU #10936 — referencia a la escritura (admin.company_deeds.id) que entró al registro cuando este
    /// adjunto es una escritura de sistema (tipos 'escritura'/'escritura_comprador'). Deja trazable cuál
    /// escritura se usó y permite "congelar" la utilizada tras la entrega del trámite. <c>null</c> en
    /// cualquier otro adjunto (FUR, certificados, cargas de usuario).
    /// </summary>
    public Guid? SourceDeedId { get; set; }

    /// <summary>
    /// HU #11313/#11316 (ADR-0042) — referencia a la versión (admin.company_personalized_documents.id)
    /// que entró al registro cuando este adjunto es un documento personalizado de compañía
    /// (<c>Source = "company"</c>). Espejo exacto de <see cref="SourceDeedId"/>. <c>null</c> en
    /// cualquier otro adjunto.
    /// </summary>
    public Guid? SourcePersonalizedDocumentId { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
