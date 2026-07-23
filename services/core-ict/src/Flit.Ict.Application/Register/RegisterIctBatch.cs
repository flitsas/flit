using System.Text.Json.Serialization;

namespace Flit.Ict.Application.Register;

/// <summary>Actor del payload de registro (vendedor/comprador/locatario). JSON snake_case (contrato v1).</summary>
public sealed record RegisterActorInput(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("document_number")] string DocumentNumber,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("first_last_name")] string FirstLastName,
    [property: JsonPropertyName("second_last_name")] string? SecondLastName = null,
    [property: JsonPropertyName("phone")] string? Phone = null,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("city")] string? City = null,
    [property: JsonPropertyName("state")] string? State = null,
    [property: JsonPropertyName("address")] string? Address = null,
    [property: JsonPropertyName("expedition_date")] string? ExpeditionDate = null);

/// <summary>Un registro (trámite) del lote. JSON snake_case (contrato v1).</summary>
public sealed record RegisterRowInput(
    [property: JsonPropertyName("transaction_type")] int TransactionType,
    [property: JsonPropertyName("transaction_operation")] int TransactionOperation,
    [property: JsonPropertyName("company_manager_document")] string? CompanyManagerDocument = null,
    [property: JsonPropertyName("manager_user")] string? ManagerUser = null,
    [property: JsonPropertyName("manager_mail")] string? ManagerMail = null,
    [property: JsonPropertyName("manager_id_transaction")] string? ManagerIdTransaction = null,
    [property: JsonPropertyName("delivery_address")] string? DeliveryAddress = null,
    [property: JsonPropertyName("plate")] string? Plate = null,
    [property: JsonPropertyName("vin")] string? Vin = null,
    [property: JsonPropertyName("traffic_secretary_code")] string? TrafficSecretaryCode = null,
    [property: JsonPropertyName("selling_date")] string? SellingDate = null,
    [property: JsonPropertyName("selling_price")] decimal? SellingPrice = null,
    [property: JsonPropertyName("process_without_attached_documents")] bool ProcessWithoutAttachedDocuments = false,
    [property: JsonPropertyName("url_web_hook")] string? UrlWebHook = null,
    [property: JsonPropertyName("seller")] IReadOnlyList<RegisterActorInput>? Seller = null,
    [property: JsonPropertyName("buyer")] IReadOnlyList<RegisterActorInput>? Buyer = null,
    [property: JsonPropertyName("lessee")] IReadOnlyList<RegisterActorInput>? Lessee = null);

public sealed record RegisterBatchCommand(IReadOnlyList<RegisterRowInput> Rows);

/// <summary>Detalle por fila (contrato v1): Status 1=ok, 2=error.</summary>
public sealed record RegisterDetail(
    [property: JsonPropertyName("Plate")] string Plate,
    [property: JsonPropertyName("Status")] int Status,
    [property: JsonPropertyName("Message")] string Message,
    [property: JsonPropertyName("TransactionFlit")] string TransactionFlit);

public sealed record RegisterBatchResult(
    [property: JsonPropertyName("TotalRows")] int TotalRows,
    [property: JsonPropertyName("TotalRowsProcessed")] int TotalRowsProcessed,
    [property: JsonPropertyName("Detail")] IReadOnlyList<RegisterDetail> Detail);

/// <summary>Opciones de ingesta (límite de lote configurable, v1 MAX_NUMBER_OF_ITEMS_TO_BE_CREATED).</summary>
public sealed class IctIngestOptions
{
    public const string SectionName = "Ingest";

    public int MaxItemsPerBatch { get; init; } = 20;
}
