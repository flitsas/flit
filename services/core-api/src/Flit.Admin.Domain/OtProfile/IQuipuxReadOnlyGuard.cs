namespace Flit.Admin.Domain.OtProfile;

/// <summary>
/// Valida si una acción de trámite está permitida bajo modo Quipux read-only (HU #10215 AC4).
/// </summary>
public interface IQuipuxReadOnlyGuard
{
    Task<QuipuxReadOnlyResult> ValidateActionAsync(
        Guid tenantId,
        string action,
        CancellationToken cancellationToken = default);
}
