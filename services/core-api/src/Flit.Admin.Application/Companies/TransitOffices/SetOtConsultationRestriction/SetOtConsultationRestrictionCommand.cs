namespace Flit.Admin.Application.Companies.TransitOffices.SetOtConsultationRestriction;

/// <summary>Comando para fijar el estado deseado de una restricción de consulta por OT (HU #10759).</summary>
public sealed class SetOtConsultationRestrictionCommand
{
    public required Guid TenantId { get; init; }

    public required Guid TransitOfficeId { get; init; }

    public required string? ConsultationKind { get; init; }

    public required bool Enabled { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que hace el cambio. Opcional.</summary>
    public Guid? ChangedBy { get; init; }

    /// <summary>Id de correlación opcional para trazabilidad de la auditoría.</summary>
    public Guid? CorrelationId { get; init; }
}
