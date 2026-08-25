using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gate de "finalizar borrador" (HU #10349, AC1). A diferencia de <see cref="SubmitGate"/>, NO exige
/// validación de identidad ni FUR: solo verifica que la captura de datos esté completa (actores de la
/// modalidad, documentos obligatorios y organismo de tránsito). Reutiliza las mismas comprobaciones de
/// completitud documental/organismo que el gate de radicado (única fuente de verdad).
///
/// Códigos de error: <c>actores_incompletos</c>, <c>documentos_incompletos</c>, <c>organismo_requerido</c>.
/// </summary>
public static class FinalizeDraftGate
{
    public const string ActoresIncompletos = "actores_incompletos";
    public const string DocumentosIncompletos = "documentos_incompletos";
    public const string OrganismoRequerido = "organismo_requerido";

    /// <summary>Evalúa el gate. La instancia debe traer cargado el grafo del wizard. Lista vacía = puede finalizar.</summary>
    /// <param name="documentosCompletosOverride">
    /// HU #10522 (RF17/RF22): completitud documental resuelta desde la matriz del gestor; <c>null</c>
    /// ⇒ se usa el gate actual del catálogo (flag OFF o sin matriz), sin regresión.
    /// </param>
    public static IReadOnlyList<string> Evaluate(ProcedureInstance instance, bool? documentosCompletosOverride = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var modalidad = instance.Family;
        var esTraspaso = modalidad == ProcedureFamily.Traspaso;

        var errors = new List<string>(3);

        if (!ActoresCompletos(instance, esTraspaso))
            errors.Add(ActoresIncompletos);
        if (!(documentosCompletosOverride ?? SubmitGate.DocumentosObligatoriosCompletos(instance)))
            errors.Add(DocumentosIncompletos);
        if (!SubmitGate.OrganismoSeleccionado(instance))
            errors.Add(OrganismoRequerido);

        return errors;
    }

    /// <summary>Matrícula requiere comprador; traspaso requiere comprador + vendedor, con datos básicos.</summary>
    private static bool ActoresCompletos(ProcedureInstance instance, bool esTraspaso)
    {
        bool Completo(string parte)
        {
            var a = instance.Actors.FirstOrDefault(x =>
                string.Equals(x.ActorType, parte, StringComparison.OrdinalIgnoreCase));
            return a is not null
                && !string.IsNullOrWhiteSpace(a.FullName)
                && !string.IsNullOrWhiteSpace(a.DocumentType)
                && !string.IsNullOrWhiteSpace(a.DocumentNumber);
        }

        return esTraspaso
            ? Completo(BiometricRules.ParteComprador) && Completo(BiometricRules.ParteVendedor)
            : Completo(BiometricRules.ParteComprador);
    }
}

/// <summary>
/// Finaliza un borrador (HU #10349, AC1): marca <c>draft_finalized_at</c> dejando el trámite en
/// <c>draft</c> a la espera de la validación de identidad async del cliente. Valida completitud de datos
/// (sin identidad ni FUR) vía <see cref="FinalizeDraftGate"/> y registra el evento <c>draft_finalizado</c>
/// en la bitácora. Idempotente: si el borrador ya estaba finalizado, devuelve el resumen sin re-sellar.
/// </summary>
public sealed class FinalizeDraftProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    ChecklistMatrixCompleteness? matrixCompleteness = null)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != TramiteEstado.Borrador)
            return (null, "not_draft");

        // Idempotencia: borrador ya finalizado → no se re-sella ni se duplica la bitácora.
        if (instance.DraftFinalizedAt is not null)
            return (CreateProcedureInstanceHandler.ToSummary(instance), null);

        // HU #10522 (RF17/RF22) — el gestor manda la completitud documental si tiene matriz (flag ON).
        var docsCompletos = matrixCompleteness is null
            ? null
            : await matrixCompleteness.TryComputeCompletoAsync(instance, tenantId, ct);
        var gateErrors = FinalizeDraftGate.Evaluate(instance, docsCompletos);
        if (gateErrors.Count > 0)
            return (null, gateErrors[0]);

        var now = DateTimeOffset.UtcNow;
        instance.DraftFinalizedAt = now;
        instance.UpdatedAt = now;

        repo.Add(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "draft_finalizado",
            Payload = JsonSerializer.Serialize(new
            {
                modalidad = instance.FamilyCode,
                draft_finalized_at = now,
            }),
            CreatedAt = now,
        });

        await repo.SaveChangesAsync(ct);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
