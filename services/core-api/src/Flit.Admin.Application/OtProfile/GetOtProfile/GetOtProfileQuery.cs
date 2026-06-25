namespace Flit.Admin.Application.OtProfile.GetOtProfile;

public sealed class GetOtProfileQuery
{
    public required Guid TenantId { get; init; }

    /// <summary>Oficina OT seleccionada en la UI (SuperAdmin navegando el hub).</summary>
    public Guid? TransitOfficeId { get; init; }
}
