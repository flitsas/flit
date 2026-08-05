using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Decisión de envío de correo de validación de identidad (HU #11263 / ADR-0039).
/// Capa PREVIA a los guards de idempotencia por parte; no los sustituye.
/// </summary>
public enum IdentitySendDecisionKind
{
    /// <summary>No corresponde enviar correo.</summary>
    NoEnviar = 0,

    /// <summary>
    /// Hay validación en vuelo con enlace vencido: no crear fila nueva; encauzar al reenvío
    /// (tope 3 / cooldown 5 min).
    /// </summary>
    EncauzarReenvio = 1,

    /// <summary>Corresponde enviar (crear / iniciar validación).</summary>
    Enviar = 2,
}

/// <summary>Origen de la validación/cobertura que bloqueó o encauzó el envío.</summary>
public static class IdentitySendOrigen
{
    public const string Baul = "baul";
    public const string Tramite = "tramite";
    public const string Standalone = "standalone";
}

/// <summary>Motivos estables para el cuerpo informativo del 409 / UI.</summary>
public static class IdentitySendMotivo
{
    public const string CoberturaBaul = "cobertura_baul";
    public const string IdentidadVigente = "identidad_vigente";
    public const string ValidacionEnVuelo = "validacion_en_vuelo";
    public const string EnlaceVencidoReenvio = "enlace_vencido_reenvio";
    public const string CorrespondeEnviar = "corresponde_enviar";
}

/// <summary>Resultado de la precedencia única de decisión de envío.</summary>
public sealed record IdentitySendDecision(
    IdentitySendDecisionKind Kind,
    string Motivo,
    string? Status = null,
    DateTimeOffset? ValidatedAt = null,
    DateTimeOffset? ValidUntil = null,
    Guid? ValidationId = null,
    string? Origen = null);

/// <summary>
/// Entrada pura al evaluador (sin I/O). El llamador resuelve baúl y carga las validaciones
/// candidatas de la persona en el tenant; este componente solo aplica la precedencia.
/// </summary>
public sealed record IdentitySendDecisionContext(
    Guid TenantId,
    string? DocumentType,
    string? DocumentNumber,
    ProcedureInstanceActor? Actor,
    bool HasBaulFirmaActivaVigente,
    IReadOnlyList<ProcedureInstanceBiometricValidation> ValidationsForPerson,
    DateTimeOffset Now);

/// <summary>
/// Componente único: ¿corresponde enviar correo de validación a esta persona?
/// Precedencia (ADR-0039 / CF-01):
/// 1. Cobertura baúl (<see cref="UseCases.ProcedureInstances.FirmaBaulCobertura.Aplica"/> + firma vigente)
/// 2. Identidad aprobada y vigente (por documento normalizado)
/// 3. Validación en vuelo con enlace no vencido
/// 4. Validación en vuelo con enlace vencido → encauzar reenvío (no crear fila)
/// 5. Enviar
/// </summary>
public static class IdentitySendDecisionEvaluator
{
    private static readonly HashSet<string> EstadosEnVuelo = new(StringComparer.OrdinalIgnoreCase)
    {
        BiometricEstados.Enviado,
        BiometricEstados.EnProceso,
        BiometricEstados.PendienteEnvio,
    };

    public static IdentitySendDecision Evaluate(IdentitySendDecisionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // 1) Baúl primero — reutiliza FirmaBaulCobertura.Aplica (prohibido copiar el predicado).
        if (UseCases.ProcedureInstances.FirmaBaulCobertura.Aplica(ctx.Actor)
            && ctx.HasBaulFirmaActivaVigente)
        {
            return new IdentitySendDecision(
                IdentitySendDecisionKind.NoEnviar,
                IdentitySendMotivo.CoberturaBaul,
                Origen: IdentitySendOrigen.Baul);
        }

        var candidates = (ctx.ValidationsForPerson ?? Array.Empty<ProcedureInstanceBiometricValidation>())
            .Where(v => DocumentMatches(v, ctx.DocumentType, ctx.DocumentNumber))
            .ToList();

        // 2) Identidad aprobada y vigente
        var vigente = candidates
            .Where(v => BiometricRules.EsAprobadaVigente(v, ctx.Now))
            .OrderByDescending(v => v.ValidatedAt ?? v.UpdatedAt)
            .FirstOrDefault();
        if (vigente is not null)
        {
            return new IdentitySendDecision(
                IdentitySendDecisionKind.NoEnviar,
                IdentitySendMotivo.IdentidadVigente,
                Status: vigente.Status,
                ValidatedAt: vigente.ValidatedAt,
                ValidUntil: vigente.ValidUntil ?? BiometricRules.FechaFinVigencia(vigente),
                ValidationId: vigente.Id,
                Origen: OrigenDe(vigente));
        }

        // 3–4) En vuelo (enviado / en_proceso / pendiente_envio)
        var enVuelo = candidates
            .Where(v => EstadosEnVuelo.Contains(v.Status))
            .OrderByDescending(v => v.UpdatedAt)
            .FirstOrDefault();
        if (enVuelo is not null)
        {
            if (enVuelo.ExpiresAt > ctx.Now)
            {
                return new IdentitySendDecision(
                    IdentitySendDecisionKind.NoEnviar,
                    IdentitySendMotivo.ValidacionEnVuelo,
                    Status: enVuelo.Status,
                    ValidationId: enVuelo.Id,
                    Origen: OrigenDe(enVuelo));
            }

            return new IdentitySendDecision(
                IdentitySendDecisionKind.EncauzarReenvio,
                IdentitySendMotivo.EnlaceVencidoReenvio,
                Status: enVuelo.Status,
                ValidationId: enVuelo.Id,
                Origen: OrigenDe(enVuelo));
        }

        // 5) Enviar
        return new IdentitySendDecision(
            IdentitySendDecisionKind.Enviar,
            IdentitySendMotivo.CorrespondeEnviar);
    }

    private static bool DocumentMatches(
        ProcedureInstanceBiometricValidation v,
        string? documentType,
        string? documentNumber) =>
        Domain.Identity.DocumentCanonicalNormalization.MatchesCanonical(
            v.DocumentType, v.DocumentNumber, documentType, documentNumber);

    private static string OrigenDe(ProcedureInstanceBiometricValidation v) =>
        v.ProcedureInstanceId is null
            ? IdentitySendOrigen.Standalone
            : IdentitySendOrigen.Tramite;
}
