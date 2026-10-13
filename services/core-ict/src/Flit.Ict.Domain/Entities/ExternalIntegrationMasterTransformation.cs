namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Transformación aplicada a un pre-trámite (contrato v1: <c>more_transaction_transaction_type</c>).
/// Puente N:M con el catálogo de transformaciones; tabla
/// <c>ict.external_integration_master_transformation_type</c>, PK compuesta (master, código).
/// </summary>
public sealed class ExternalIntegrationMasterTransformation
{
    public Guid MasterId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// Código de la transformación según el catálogo ICT Tipo Trámite (Bug #13445): 5=blindaje,
    /// 6=cambio de carrocería, 7=cambio de color, 9=conversión de combustible.
    /// </summary>
    public int IdTransformationType { get; set; }

    /// <summary>Valor libre del gestor (p.ej. el color aplicado).</summary>
    public string Description { get; set; } = string.Empty;
}
