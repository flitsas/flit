namespace Flit.Admin.Application.OtRequirements.GetOtRequirements;

public sealed class GetOtRequirementsQuery
{
    public required Guid TenantId { get; init; }

    /// <summary>Oficina OT seleccionada en la UI (SuperAdmin navegando el hub).</summary>
    public Guid? TransitOfficeId { get; init; }
}
