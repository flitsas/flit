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

    public int TransactionOperation { get; set; }

    public string? TransactionFlit { get; set; }

    public int TransactionType { get; set; }

    public string Plate { get; set; } = string.Empty;

    public string? Vin { get; set; }

    public string SellingDate { get; set; } = string.Empty;

    public decimal SellingPrice { get; set; }

    public string TrafficSecretaryCode { get; set; } = string.Empty;

    public string UrlWebHook { get; set; } = string.Empty;

    public bool ClosedDocument { get; set; }

    public bool ProcessWithoutAttachedDocuments { get; set; }

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
}
