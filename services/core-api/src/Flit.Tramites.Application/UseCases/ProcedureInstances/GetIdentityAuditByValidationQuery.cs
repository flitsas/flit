using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bitácora (solo lectura) de una validación de identidad SIN depender de <c>instanceId</c> — CF-07
/// (Feature #11004, ADR-0036). Equivalente a <see cref="GetIdentityAuditHandler"/> (por-instancia) pero
/// sirve tanto a prevalidaciones standalone como a validaciones de trámite: reutiliza el mismo query
/// (<c>ListIdentityAuditByValidationAsync</c>) y el mismo saneo (sin secretos ni PII cruda). El tenant es
/// la única frontera dura (mismo criterio 404 uniforme, sin filtrar existencia cross-tenant).
/// Autorización: genérica del módulo (D2) — cualquier usuario autenticado del tenant, no solo SuperAdmin.
/// </summary>
public sealed class GetIdentityAuditByValidationHandler(IProcedureInstanceRepository repo)
{
    public async Task<(IdentityAuditResponse? Result, string? Error)> HandleAsync(
        Guid tenantId, Guid validationId, CancellationToken ct = default)
    {
        var v = await repo.GetBiometricByIdAsync(validationId, ct);
        if (v is null || v.TenantId != tenantId)
            return (null, "not_found");

        var events = await repo.ListIdentityAuditByValidationAsync(validationId, ct);
        var dtos = events
            .Select(e => new IdentityAuditEventDto(
                e.OccurredAt, e.Stage, e.Outcome, e.HttpStatus,
                e.SignaturePresent, e.SecretPresent, e.DecryptOk,
                e.ProviderStatus, e.ErrorType, e.Message))
            .ToList();

        // Sin instanceId no aplica el concepto de "referenciada desde OTRO trámite" (eso solo tiene
        // sentido cuando se consulta relativo a una instancia concreta): siempre false aquí.
        return (new IdentityAuditResponse(validationId, dtos), null);
    }
}
