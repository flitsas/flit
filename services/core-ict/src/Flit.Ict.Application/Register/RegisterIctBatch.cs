using System.Text.Json.Serialization;

namespace Flit.Ict.Application.Register;

/// <summary>
/// Representante legal de un actor (contrato v1: <c>seller/legal_representative</c>). Obligatorio
/// cuando el actor es un NIT; los nombres de campo repiten el prefijo, como en v1.
/// </summary>
public sealed record RegisterLegalRepresentativeInput(
    [property: JsonPropertyName("legal_representative_document_type")] string? DocumentType = null,
    [property: JsonPropertyName("legal_representative_document_number")] string? DocumentNumber = null,
    [property: JsonPropertyName("legal_representative_name")] string? Name = null,
    [property: JsonPropertyName("legal_representative_first_last_name")] string? FirstLastName = null,
    [property: JsonPropertyName("legal_representative_second_last_name")] string? SecondLastName = null,
    [property: JsonPropertyName("legal_representative_phone")] string? Phone = null,
    [property: JsonPropertyName("legal_representative_email")] string? Email = null,
    [property: JsonPropertyName("legal_representative_city")] string? City = null,
    [property: JsonPropertyName("legal_representative_state")] string? State = null,
    [property: JsonPropertyName("legal_representative_address")] string? Address = null);

/// <summary>Mandante/apoderado de un actor (contrato v1: <c>seller/principal_mandante</c>).</summary>
public sealed record RegisterPrincipalMandanteInput(
    [property: JsonPropertyName("principal_mandante_document_type")] string? DocumentType = null,
    [property: JsonPropertyName("principal_mandante_document_number")] string? DocumentNumber = null,
    [property: JsonPropertyName("principal_mandante_name")] string? Name = null,
    [property: JsonPropertyName("principal_mandante_first_last_name")] string? FirstLastName = null,
    [property: JsonPropertyName("principal_mandante_second_last_name")] string? SecondLastName = null,
    [property: JsonPropertyName("principal_mandante_email")] string? Email = null);

/// <summary>
/// Transformación declarada por el gestor (contrato v1: <c>more_transaction_transaction_type</c>).
/// OJO: dentro de este arreglo el contrato usa camelCase (<c>transactionType</c>), a diferencia del
/// resto del payload que es snake_case. <c>transactionType</c> es el CÓDIGO RUNT (5/9/17).
/// </summary>
public sealed record RegisterTransformationInput(
    [property: JsonPropertyName("transactionType")] int TransactionType,
    [property: JsonPropertyName("description")] string? Description = null);

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
    [property: JsonPropertyName("expedition_date")] string? ExpeditionDate = null,
    [property: JsonPropertyName("legal_representative")] RegisterLegalRepresentativeInput? LegalRepresentative = null,
    [property: JsonPropertyName("principal_mandante")] RegisterPrincipalMandanteInput? PrincipalMandante = null);

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
    // Banderas de comportamiento (v1). send_automatic_traffic_secretary por defecto true.
    [property: JsonPropertyName("priority")] bool Priority = false,
    [property: JsonPropertyName("transaction_flit")] string? TransactionFlit = null,
    [property: JsonPropertyName("starts_procedure_in_paused")] bool StartsProcedureInPaused = false,
    [property: JsonPropertyName("observation_when_paused")] string? ObservationWhenPaused = null,
    [property: JsonPropertyName("send_automatic_traffic_secretary")] bool SendAutomaticTrafficSecretary = true,
    [property: JsonPropertyName("plate_assignment_type")] short? PlateAssignmentType = null,
    // Compañía relacionada (servicio público).
    [property: JsonPropertyName("related_company_company_document")] string? RelatedCompanyDocument = null,
    [property: JsonPropertyName("related_company_company_name")] string? RelatedCompanyName = null,
    // Limitación / garantía mobiliaria (prenda).
    [property: JsonPropertyName("limitations_operation_type")] short? LimitationsOperationType = null,
    [property: JsonPropertyName("limitations_creditor")] string? LimitationsCreditor = null,
    [property: JsonPropertyName("limitations_creditor_document_type")] string? LimitationsCreditorDocumentType = null,
    [property: JsonPropertyName("limitations_creditor_document_number")] string? LimitationsCreditorDocumentNumber = null,
    [property: JsonPropertyName("limitations_inscription_date")] string? LimitationsInscriptionDate = null,
    // Otros trámites: blindaje y nuevo combustible.
    [property: JsonPropertyName("armor_level_number_id")] short? ArmorLevelNumberId = null,
    [property: JsonPropertyName("new_vehicle_fuel_type")] short? NewVehicleFuelType = null,
    [property: JsonPropertyName("more_transaction_transaction_type")] IReadOnlyList<RegisterTransformationInput>? MoreTransactionTransactionType = null,
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
