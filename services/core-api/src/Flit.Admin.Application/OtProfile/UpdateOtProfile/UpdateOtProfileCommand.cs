namespace Flit.Admin.Application.OtProfile.UpdateOtProfile;

public sealed class UpdateOtProfileCommand
{
    public required Guid TenantId { get; init; }

    public Guid? ChangedBy { get; init; }

    public required UpdateOtProfileRequest Request { get; init; }
}

public sealed class UpdateOtProfileRequest
{
    public string? OperationMode { get; init; }

    public bool? QuipuxReadOnly { get; init; }

    /// <summary>Ignorado — el tenant proviene del token JWT (AC5).</summary>
    public Guid? TenantId { get; init; }
}
