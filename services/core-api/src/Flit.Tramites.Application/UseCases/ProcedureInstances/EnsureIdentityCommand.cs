using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Posibles desenlaces de "asegurar identidad" de una parte (HU #10350).</summary>
public static class EnsureIdentityOutcomes
{
    /// <summary>El trámite ya tiene una validación APROBADA y VIGENTE para la parte → nada que hacer.</summary>
    public const string YaVigente = "ya_vigente";

    /// <summary>El trámite ya tiene una validación en curso (enviada/en proceso) → se espera su resultado.</summary>
    public const string EnProceso = "en_proceso";

    /// <summary>Se reutilizó (clonó) una validación vigente de la persona desde otro trámite del tenant.</summary>
    public const string Reusada = "reusada";

    /// <summary>
    /// El actor es jurídico (NIT) y el tenant tiene una firma de baúl ACTIVA+VIGENTE para su NIT
    /// (ADR-0025 §4, HU #10645, R14). La identidad queda cubierta por la firma precargada → NO se
    /// exige una nueva validación. Precedencia D8: baúl → identidad vigente reutilizable → manual.
    /// </summary>
    public const string FirmaBaul = "firma_baul";

    /// <summary>No hay validación vigente → el cliente debe validar (el front la dispara automáticamente).</summary>
    public const string RequiereValidacion = "requiere_validacion";

    /// <summary>La parte aún no tiene actor con documento → no se puede evaluar la identidad todavía.</summary>
    public const string SinActor = "sin_actor";
}

/// <summary>Resultado de <see cref="EnsureIdentityHandler"/>: el desenlace y, si aplica, la validación resultante.</summary>
public sealed record EnsureIdentityResult(string Outcome, Guid? ValidationId = null);

/// <summary>
/// HU #10350 — al guardar/continuar el paso de una parte (comprador/vendedor), asegura su validación de
/// identidad sin esperar a un clic manual en "Validar identidad":
/// <list type="number">
///   <item>Si el trámite ya tiene una validación VIGENTE (aprobada ≤30 días) o EN CURSO para la parte → no-op.</item>
///   <item>Si la persona (tipoDoc+documento) tiene una validación VIGENTE en otro trámite del tenant → la
///         <b>reutiliza</b>, clonándola a este trámite (hereda <c>ValidadoAt</c>, por lo que mantiene el
///         mismo vencimiento) → la identidad queda aprobada sin revalidar.</item>
///   <item>Si no hay ninguna vigente → devuelve <c>requiere_validacion</c> para que el cliente sea enviado a
///         validar (el frontend dispara la validación provider-aware automáticamente).</item>
/// </list>
/// NO inicia la validación aquí (eso lo hace el flujo provider-aware existente); este handler sólo decide y
/// reutiliza. Idempotente: una segunda llamada ve la validación ya clonada/en curso y responde no-op.
/// </summary>
public sealed class EnsureIdentityHandler(
    IProcedureInstanceRepository repo,
    ISignatureVaultPolicy? vaultPolicy = null)
{
    // ADR-0025 §4 — política del baúl de firmas (activa el flag signature_vault_enabled). Default seguro
    // (NUNCA resuelve firma) cuando no se cablea, para no alterar los tests de identidad existentes.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    public async Task<(EnsureIdentityResult? Result, string? Error)> HandleAsync(
        Guid id, Guid tenantId, string? parte, CancellationToken ct = default)
    {
        var normalized = NormalizeParte(parte);
        if (normalized is null)
            return (null, "parte_invalida");

        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, normalized, StringComparison.OrdinalIgnoreCase));
        // Documento del SUJETO de identidad (HU #10688): el RL en PJ, el actor en PN. Sin documento del
        // sujeto no hay identidad que asegurar (PJ sin RL con documento cae aquí, como el actor sin documento).
        var subject = actor is null ? null : IdentitySubjectResolver.For(actor);
        if (subject is null
            || string.IsNullOrWhiteSpace(subject.NumeroDocumento)
            || string.IsNullOrWhiteSpace(subject.TipoDocumento))
        {
            return (new EnsureIdentityResult(EnsureIdentityOutcomes.SinActor), null);
        }

        var now = DateTimeOffset.UtcNow;
        var tipoActual = subject.TipoDocumento.Trim();
        var docActual = subject.NumeroDocumento.Trim();

        // 0) Si la parte CAMBIÓ de persona (documento distinto al actor actual), sus validaciones previas
        // pertenecen a OTRA persona y dejan de aplicar: se marcan EXPIRADO para que el gate de identidad no
        // las cuente (bugfix HU #10350 — al cambiar el comprador/vendedor no debe quedar "verificada" la
        // identidad anterior). El gate ya excluye los estados no-aprobados, así no toca la lógica de gating.
        var changed = false;
        foreach (var v in instance.BiometricValidations.Where(v =>
                     string.Equals(v.PartyRole, normalized, StringComparison.OrdinalIgnoreCase)
                     && v.Status != BiometricEstados.Expirado
                     && !DocCoincide(v, tipoActual, docActual)))
        {
            v.Status = BiometricEstados.Expirado;
            v.UpdatedAt = now;
            changed = true;
        }

        // 1) ¿El trámite ya tiene una validación utilizable para ESTA persona (documento actual)?
        var locales = instance.BiometricValidations
            .Where(v => string.Equals(v.PartyRole, normalized, StringComparison.OrdinalIgnoreCase)
                && DocCoincide(v, tipoActual, docActual))
            .ToList();
        if (locales.Any(v => BiometricRules.EsAprobadaVigente(v, now)))
        {
            if (changed) await repo.SaveChangesAsync(ct);
            return (new EnsureIdentityResult(EnsureIdentityOutcomes.YaVigente), null);
        }
        if (locales.Any(v => v.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso))
        {
            if (changed) await repo.SaveChangesAsync(ct);
            return (new EnsureIdentityResult(EnsureIdentityOutcomes.EnProceso), null);
        }

        // 1.5) BAÚL DE FIRMAS (ADR-0025 §4, HU #10645, R14). Si el actor es JURÍDICO (NIT) y el tenant
        // tiene una firma de baúl ACTIVA+VIGENTE para su NIT, la identidad queda cubierta por la firma
        // precargada → outcome firma_baul, sin exigir validación (ValidationId null). Precedencia D8: el
        // baúl va DESPUÉS de una validación propia vigente/en curso del trámite (ya resuelta arriba) pero
        // ANTES de la reutilización cross-trámite por documento (paso 2, reusada). Solo NIT: las personas
        // naturales caen al flujo de identidad sin cambios. La política es null-safe cuando el baúl está
        // deshabilitado o no hay firma vigente (devuelve null → se sigue al paso 2).
        if (EsActorJuridico(tipoActual))
        {
            var vaultMatch = await _vaultPolicy.ResolveAsync(tenantId, docActual, ct);
            if (vaultMatch is not null)
            {
                if (changed) await repo.SaveChangesAsync(ct);
                return (new EnsureIdentityResult(EnsureIdentityOutcomes.FirmaBaul), null);
            }
        }

        // 2) ¿La persona (documento actual) tiene una identidad vigente aprobada en otro trámite del tenant?
        // → se REFERENCIA (HU #10350 rediseño): NO se clona ni se crea fila. La identidad se valida UNA sola
        // vez por persona y sirve para N trámites hasta que venza; los gates y la vista la resuelven por
        // documento (FindVigenteApprovedByDocument). Devolvemos el id de la validación ORIGEN como referencia.
        var source = await repo.FindVigenteApprovedByDocumentAsync(tenantId, tipoActual, docActual, now, ct);
        if (source is not null)
        {
            if (changed) await repo.SaveChangesAsync(ct);
            return (new EnsureIdentityResult(EnsureIdentityOutcomes.Reusada, source.Id), null);
        }

        // 3) No hay vigente → el cliente debe validar (el frontend dispara la validación automáticamente).
        // Persiste la invalidación de las validaciones de la persona anterior (si las hubo).
        if (changed) await repo.SaveChangesAsync(ct);
        return (new EnsureIdentityResult(EnsureIdentityOutcomes.RequiereValidacion), null);
    }

    /// <summary>
    /// ¿El actor es una persona JURÍDICA (NIT)? Solo estos consumen el baúl de firmas (ADR-0025 §4).
    /// Acepta "NIT" y "N" (código corto), sin distinguir mayúsculas ni espacios.
    /// </summary>
    private static bool EsActorJuridico(string documentType)
    {
        var t = documentType.Trim();
        return string.Equals(t, "NIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "N", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>¿La validación corresponde al documento (tipo + número) del actor actual de la parte?</summary>
    private static bool DocCoincide(ProcedureInstanceBiometricValidation v, string tipoDoc, string documento) =>
        string.Equals(v.DocumentNumber?.Trim(), documento, StringComparison.OrdinalIgnoreCase)
        && string.Equals(v.DocumentType?.Trim(), tipoDoc, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeParte(string? parte)
    {
        var p = parte?.Trim().ToLowerInvariant();
        return p is BiometricRules.ParteComprador or BiometricRules.ParteVendedor ? p : null;
    }
}
