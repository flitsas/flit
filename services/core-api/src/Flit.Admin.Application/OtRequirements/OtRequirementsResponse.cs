using Flit.Admin.Domain.OtRequirements;

namespace Flit.Admin.Application.OtRequirements;

/// <summary>Respuesta de los requisitos configurables de un OT (HU #10546).</summary>
public sealed record OtRequirementsResponse(
    Guid TransitOfficeId,
    bool RequiresRnmc,
    bool AllowPlatePreassign,
    bool IdentityValidationEnabled);

public static class OtRequirementsMapper
{
    public static OtRequirementsResponse ToResponse(Domain.OtRequirements.OtRequirements requirements) =>
        new(
            requirements.TransitOfficeId,
            requirements.RequiresRnmc,
            requirements.AllowPlatePreassign,
            requirements.IdentityValidationEnabled);
}
