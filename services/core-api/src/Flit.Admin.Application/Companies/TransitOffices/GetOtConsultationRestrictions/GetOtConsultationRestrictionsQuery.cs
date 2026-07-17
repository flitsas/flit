namespace Flit.Admin.Application.Companies.TransitOffices.GetOtConsultationRestrictions;

/// <summary>Consulta de las restricciones de consulta por OT configuradas para un tenant (AC1/AC5).</summary>
public sealed class GetOtConsultationRestrictionsQuery
{
    public required Guid TenantId { get; init; }
}
