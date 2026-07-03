using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Una fila de la bitácora de identidad, expuesta a la API (sin secretos ni PII).</summary>
public sealed record IdentityAuditEventDto(
    DateTimeOffset OccurredAt,
    string Stage,
    string Outcome,
    int? HttpStatus,
    bool? SignaturePresent,
    bool? SecretPresent,
    bool? DecryptOk,
    string? ProviderStatus,
    string? ErrorType,
    string? Message);

public sealed record IdentityAuditResponse(Guid ValidationId, IReadOnlyList<IdentityAuditEventDto> Events);

/// <summary>
/// Devuelve la bitácora (solo lectura) del ciclo de una validación de identidad: envío, llegada del webhook,
/// si se descifró el secreto o no, firma, resultado y reconciliaciones. Para diagnosticar "qué pasó" desde la
/// API sin entrar a la BD ni a los logs del pod. Tenant-scoped: valida que la validación sea del tenant/instancia.
/// </summary>
public sealed class GetIdentityAuditHandler(IProcedureInstanceRepository repo)
{
    public async Task<(IdentityAuditResponse? Result, string? Error)> HandleAsync(
        Guid instanceId, Guid tenantId, Guid validationId, CancellationToken ct = default)
    {
        var v = await repo.GetBiometricByIdAsync(validationId, ct);
        if (v is null || v.TenantId != tenantId || v.ProcedureInstanceId != instanceId)
            return (null, "not_found");

        var events = await repo.ListIdentityAuditByValidationAsync(validationId, ct);
        var dtos = events
            .Select(e => new IdentityAuditEventDto(
                e.OccurredAt, e.Stage, e.Outcome, e.HttpStatus,
                e.SignaturePresent, e.SecretPresent, e.DecryptOk,
                e.ProviderStatus, e.ErrorType, e.Message))
            .ToList();

        return (new IdentityAuditResponse(validationId, dtos), null);
    }
}
