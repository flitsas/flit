using System.Text.Json.Serialization;
using Flit.Admin.Domain.OtDocumentPrecedence;

namespace Flit.Admin.Application.OtDocumentPrecedence;

public sealed class OtDocumentPrecedenceResponse
{
    [JsonPropertyName("document_type_id")]
    public Guid DocumentTypeId { get; init; }

    [JsonPropertyName("document_code")]
    public string DocumentCode { get; init; } = string.Empty;

    [JsonPropertyName("document_name")]
    public string DocumentName { get; init; } = string.Empty;

    [JsonPropertyName("sort_order")]
    public short SortOrder { get; init; }

    /// <summary>HU #11181 — lo produce FLIT; el gestor no lo adjunta.</summary>
    [JsonPropertyName("is_system_generated")]
    public bool IsSystemGenerated { get; init; }

    /// <summary>HU #11182 — el OT ya guardó una posición para este documento.</summary>
    [JsonPropertyName("is_configured")]
    public bool IsConfigured { get; init; }
}

public sealed class UpdateOtDocumentPrecedenceRequest
{
    [JsonPropertyName("procedure_type_id")]
    public Guid ProcedureTypeId { get; set; }

    public IReadOnlyList<OtDocumentPrecedenceOrderRequest> Items { get; set; } =
        Array.Empty<OtDocumentPrecedenceOrderRequest>();
}

public sealed class OtDocumentPrecedenceOrderRequest
{
    [JsonPropertyName("document_type_id")]
    public Guid DocumentTypeId { get; set; }

    [JsonPropertyName("sort_order")]
    public short SortOrder { get; set; }
}

internal static class OtDocumentPrecedenceMapper
{
    public static OtDocumentPrecedenceResponse ToResponse(OtDocumentPrecedenceItem item) => new()
    {
        DocumentTypeId = item.DocumentTypeId,
        DocumentCode = item.DocumentCode,
        DocumentName = item.DocumentName,
        SortOrder = item.SortOrder,
        IsSystemGenerated = item.IsSystemGenerated,
        IsConfigured = item.IsConfigured,
    };
}
