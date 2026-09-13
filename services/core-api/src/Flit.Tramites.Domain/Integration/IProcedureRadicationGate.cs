namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Validación previa a la creación de trámite: OT permitido y compañía/red activa (HU #12348, #12409).
/// </summary>
public interface IProcedureRadicationGate
{
    Task<ProcedureRadicationGateResult> ValidateCreateAsync(
        Guid tenantId,
        Guid userId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

public sealed record ProcedureRadicationGateResult(bool IsAllowed, string? ErrorCode);

public static class ProcedureRadicationDenialReasons
{
    public const string OtNotPermitted = "ot_not_permitted";

    public const string NetworkInactive = "network_inactive";

    public const string TenantInactive = "tenant_inactive";
}
