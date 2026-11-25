using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
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
    string? Message,
    // Bug #14634 — motivo YA homologado (lista cerrada, HU #13299) de ESTE evento puntual de rechazo
    // manual: a diferencia de BiometricValidationDto.RejectionReason (que la siguiente captura borra),
    // este queda fijo en la bitácora, así que un hito histórico sigue mostrando por qué se rechazó
    // aunque la validación ya se haya vuelto a intentar o aprobar. Ya es texto seguro (no delata el
    // mecanismo), así que se expone igual a todos (ver IdentityAuditParaCliente.Equivalente).
    string? RejectionReasonLabel = null);

internal static class IdentityAuditEventMapper
{
    private static readonly Regex MotivoEnDetalle = new(@"motivo=([^;]+)", RegexOptions.Compiled);

    public static IdentityAuditEventDto ToDto(IdentityValidationAuditEvent e) => new(
        e.OccurredAt, e.Stage, e.Outcome, e.HttpStatus,
        e.SignaturePresent, e.SecretPresent, e.DecryptOk,
        e.ProviderStatus, e.ErrorType, e.Message,
        RejectionReasonLabel: RechazoManualLabel(e));

    private static string? RechazoManualLabel(IdentityValidationAuditEvent e)
    {
        if (!string.Equals(e.Stage, IdentityValidationAuditStages.ManualRechazado, StringComparison.Ordinal))
            return null;
        var match = e.Detail is null ? null : MotivoEnDetalle.Match(e.Detail);
        return match is { Success: true } ? ManualRejectionReasons.LabelFor(match.Groups[1].Value.Trim()) : null;
    }
}

/// <summary>
/// Respuesta de la bitácora. <paramref name="ReferencedFromOtherProcedure"/> es true cuando la
/// identidad está "reutilizada": la validación es del mismo tenant/cliente pero se realizó en OTRO
/// trámite (HU #10350) y aquí solo se referencia. En ese caso la bitácora existe y es válida (es la
/// misma fila de validación); la UI lo explica en vez de mostrar un error de "no encontrada".
/// </summary>
public sealed record IdentityAuditResponse(
    Guid ValidationId,
    IReadOnlyList<IdentityAuditEventDto> Events,
    bool ReferencedFromOtherProcedure = false);

/// <summary>
/// Devuelve la bitácora (solo lectura) del ciclo de una validación de identidad: envío, llegada del webhook,
/// si se descifró el secreto o no, firma, resultado y reconciliaciones. Para diagnosticar "qué pasó" desde la
/// API sin entrar a la BD ni a los logs del pod. Tenant-scoped: la validación debe ser del mismo tenant.
/// </summary>
public sealed class GetIdentityAuditHandler(IProcedureInstanceRepository repo)
{
    public async Task<(IdentityAuditResponse? Result, string? Error)> HandleAsync(
        Guid instanceId, Guid tenantId, Guid validationId, CancellationToken ct = default)
    {
        var v = await repo.GetBiometricByIdAsync(validationId, ct);
        // "No encontrada" real: no existe o es de otro tenant. El tenant sigue siendo la frontera dura.
        if (v is null || v.TenantId != tenantId)
            return (null, "not_found");

        // HU #10350 — identidad reutilizada ("apalancada"): la validación es del mismo tenant/cliente
        // pero pertenece a OTRO trámite (aquí solo se referencia). No es un error: es la misma fila de
        // validación y su bitácora es la real; se marca para que la UI lo explique. La bitácora no
        // contiene PII ni secretos (ya saneada), por lo que exponerla dentro del mismo tenant es seguro.
        var referenciada = v.ProcedureInstanceId != instanceId;

        var events = await repo.ListIdentityAuditByValidationAsync(validationId, ct);
        var dtos = events.Select(IdentityAuditEventMapper.ToDto).ToList();

        return (new IdentityAuditResponse(validationId, dtos, referenciada), null);
    }
}
