namespace Flit.Ict.Domain.Entities;

/// <summary>Catálogo de documentos permitidos (<c>ict.external_integration_allowed_documents</c>).</summary>
public sealed class AllowedDocument
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>Documento obligatorio/opcional por tipo de trámite (<c>ict.external_integration_configuration_documents</c>).</summary>
public sealed class ConfigurationDocument
{
    public Guid Id { get; set; }

    public short ExternalTransactionType { get; set; }

    public int IdEiad { get; set; }

    public bool IsMandatory { get; set; } = true;
}

/// <summary>Adjunto del pre-trámite (metadata; el binario vive en el File Manager).</summary>
public sealed class TransactionAttachment : AuditableEntity
{
    public Guid MasterId { get; set; }

    public Guid TenantId { get; set; }

    public int IdAttachment { get; set; }

    public string Filename { get; set; } = string.Empty;

    public string MimeType { get; set; } = "application/pdf";

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;
}
