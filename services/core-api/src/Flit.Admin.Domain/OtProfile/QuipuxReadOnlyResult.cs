namespace Flit.Admin.Domain.OtProfile;

/// <summary>Resultado de la verificación de acciones en modo QX read-only (AC4).</summary>
public sealed record QuipuxReadOnlyResult(bool IsAllowed, string? ErrorCode = null)
{
    public static QuipuxReadOnlyResult Allowed() => new(true);

    public static QuipuxReadOnlyResult Forbidden() => new(false, "QUIPUX_READONLY");
}
