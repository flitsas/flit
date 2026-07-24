namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Transformación aplicada a un pre-trámite (contrato v1: <c>more_transaction_transaction_type</c>).
/// Puente N:M con el catálogo de transformaciones RUNT; tabla
/// <c>ict.external_integration_master_transformation_type</c>, PK compuesta (master, código RUNT).
/// </summary>
public sealed class ExternalIntegrationMasterTransformation
{
    public Guid MasterId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Código RUNT de la transformación (5=color, 9=transformación, 17=carrocería).</summary>
    public int IdTransformationType { get; set; }

    /// <summary>Valor libre del gestor (p.ej. el color aplicado).</summary>
    public string Description { get; set; } = string.Empty;
}
