namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Pre-trámite (staging) de la integración. Tabla <c>ict.external_integration_master</c>.
/// Conserva los nombres de columna v1 (schema híbrido) para que los stored procedures de
/// validación se porten con mínima fricción. <c>company_manager_id</c> de v1 se reemplaza por
/// <see cref="TenantId"/>. <c>process_status_id</c> sigue siendo el estado INTERNO del pipeline
/// (lo leen/escriben los SP); el estado hacia el cliente se proyecta con IctEstado.
/// </summary>
public sealed class ExternalIntegrationMaster : AuditableEntity
{
    public Guid TenantId { get; set; }

    public string CompanyManagerDocument { get; set; } = string.Empty;

    public string ManagerUser { get; set; } = string.Empty;

    public string ManagerMail { get; set; } = string.Empty;

    public bool Priority { get; set; }

    public string DeliveryAddress { get; set; } = string.Empty;

    public string ManagerIdTransaction { get; set; } = string.Empty;

    /// <summary>
    /// Identificador numérico secuencial del trámite asignado por FLIT (llave pública, paridad v1).
    /// Lo genera la secuencia <c>ict.transaction_number_seq</c> en la BD; EF lo lee tras el INSERT.
    /// La PK sigue siendo <see cref="AuditableEntity.Id"/> (uuid).
    /// </summary>
    public long TransactionNumber { get; set; }

    public int TransactionOperation { get; set; }

    public string? TransactionFlit { get; set; }

    public int TransactionType { get; set; }

    public string Plate { get; set; } = string.Empty;

    public string? Vin { get; set; }

    public string SellingDate { get; set; } = string.Empty;

    public decimal SellingPrice { get; set; }

    public string TrafficSecretaryCode { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del organismo de tránsito de matrícula devuelto por el RUNT (consulta VEHICLE). En traspaso
    /// (3/4) el OT lo fija el RUNT, no el cliente (paridad v1): el orquestador lo captura y core-api lo
    /// resuelve al <c>transit_office_id</c> del catálogo al materializar. Null hasta que se consulta.
    /// </summary>
    public string? RuntTransitOfficeName { get; set; }

    public string UrlWebHook { get; set; } = string.Empty;

    public bool ClosedDocument { get; set; }

    public bool ProcessWithoutAttachedDocuments { get; set; }

    // --- Banderas de comportamiento del trámite resultante (contrato v1) ---
    /// <summary>El borrador se crea pausado (se refleja en el trámite de core-api).</summary>
    public bool StartsProcedureInPaused { get; set; }

    public string? ObservationWhenPaused { get; set; }

    /// <summary>Envío automático al OT cuando el borrador cumple condiciones. Por defecto true en v1.</summary>
    public bool SendAutomaticTrafficSecretary { get; set; } = true;

    public short? PlateAssignmentType { get; set; }

    // --- Compañía relacionada (matrícula de servicio público) ---
    public string? RelatedCompanyDocument { get; set; }

    public string? RelatedCompanyName { get; set; }

    // --- Limitación a la propiedad / garantía mobiliaria (prenda) ---
    public short? LimitationsOperationType { get; set; }

    public string? LimitationsCreditor { get; set; }

    public string? LimitationsCreditorDocumentType { get; set; }

    public string? LimitationsCreditorDocumentNumber { get; set; }

    public string? LimitationsInscriptionDate { get; set; }

    // --- Otros trámites: blindaje y conversión de combustible ---
    public short? ArmorLevelNumberId { get; set; }

    public short? NewVehicleFuelType { get; set; }

    // --- Estado interno del pipeline (motor de los SP) ---
    public short ProcessStatusId { get; set; } = 1;

    public short BusinessValidation { get; set; }

    public DateTime? BusinessDateValidation { get; set; }

    public string BusinessCommentsValidation { get; set; } = string.Empty;

    public short ExternalValidation { get; set; }

    public DateTime? ExternalDateValidation { get; set; }

    public string ExternalCommentsValidation { get; set; } = string.Empty;

    // --- Correlación con el trámite materializado en core-api ---
    public Guid? ProcedureInstanceId { get; set; }

    public ICollection<ExternalIntegrationActor> Actors { get; } = [];

    /// <summary>Transformaciones RUNT declaradas por el gestor (more_transaction_transaction_type).</summary>
    public ICollection<ExternalIntegrationMasterTransformation> Transformations { get; } = [];

    /// <summary>
    /// Adjuntos del pre-trámite (metadata; el binario vive en el File Manager corporativo por
    /// <c>storage_path</c>). Se materializan al borrador por REFERENCIA (no se re-suben bytes).
    /// </summary>
    public ICollection<TransactionAttachment> Attachments { get; } = [];
}
