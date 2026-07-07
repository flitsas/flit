namespace Flit.Admin.Application.OtRequirements.UpdateOtRequirements;

public sealed class UpdateOtRequirementsCommand
{
    public required Guid TenantId { get; init; }

    public Guid? ChangedBy { get; init; }

    public required UpdateOtRequirementsRequest Request { get; init; }
}

/// <summary>
/// Requisitos a persistir. Los campos nulos conservan el valor actual (permite que la UI conmute
/// un requisito sin reenviar el resto).
/// </summary>
public sealed class UpdateOtRequirementsRequest
{
    public bool? RequiresRnmc { get; init; }

    public bool? AllowPlatePreassign { get; init; }

    public bool? IdentityValidationEnabled { get; init; }

    /// <summary>Ignorado — el tenant proviene del token JWT.</summary>
    public Guid? TenantId { get; init; }
}
