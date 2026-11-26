using System.Text.Json.Serialization;
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
    string? RejectionReasonLabel = null,
    // Quién activó (manual_activado) o aprobó/rechazó (manual_aprobado/manual_rechazado) (nombre, correo, rol): SOLO para Super Admin.
    // Se resuelve al leer (la auditoría no guarda PII); null para cualquier otro caller.
    ReviewerInfo? Revisor = null,
    // Solo uso interno (resolver al revisor): nunca se serializa.
    [property: JsonIgnore] Guid? RevisorId = null);

/// <summary>Completa <see cref="IdentityAuditEventDto.Revisor"/> para el Super Admin resolviendo el usuario al leer.</summary>
public static class IdentityAuditRevisor
{
    private static readonly Regex UsuarioEnDetalle = new(@"usuario=([0-9a-fA-F-]{36})", RegexOptions.Compiled);

    /// <summary>
    /// Extrae el guid de <c>usuario=&lt;guid&gt;</c> del Detail de un evento manual_activado (quien activó el flujo manual) o
    /// manual_aprobado/manual_rechazado (quien lo resolvió).
    /// </summary>
    public static Guid? ExtraerUsuario(string stage, string? detail)
    {
        if (stage is not (IdentityValidationAuditStages.ManualActivado
            or IdentityValidationAuditStages.ManualAprobado
            or IdentityValidationAuditStages.ManualRechazado))
            return null;
        var m = detail is null ? null : UsuarioEnDetalle.Match(detail);
        return m is { Success: true } && Guid.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>
    /// Rellena <c>Revisor</c> en los eventos manual_activado/manual_aprobado/manual_rechazado. El llamador DEBE haber verificado que es Super Admin.
    /// </summary>
    public static async Task<IdentityAuditResponse> AplicarAsync(
        IdentityAuditResponse response, IReviewerDirectory directory, CancellationToken ct = default)
    {
        var ids = response.Events.Where(e => e.RevisorId is not null).Select(e => e.RevisorId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return response;

        var map = await directory.ResolveAsync(ids, ct).ConfigureAwait(false);
        var events = response.Events
            .Select(e => e.RevisorId is { } id
                ? e with { Revisor = map.TryGetValue(id, out var info) ? info : new ReviewerInfo(null, null, null) }
                : e)
            .ToList();
        return response with { Events = events };
    }
}

internal static class IdentityAuditEventMapper
{
    private static readonly Regex MotivoEnDetalle = new(@"motivo=([^;]+)", RegexOptions.Compiled);

    public static IdentityAuditEventDto ToDto(IdentityValidationAuditEvent e) => new(
        e.OccurredAt, e.Stage, e.Outcome, e.HttpStatus,
        e.SignaturePresent, e.SecretPresent, e.DecryptOk,
        e.ProviderStatus, e.ErrorType, e.Message,
        RejectionReasonLabel: RechazoManualLabel(e),
        RevisorId: IdentityAuditRevisor.ExtraerUsuario(e.Stage, e.Detail));

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
