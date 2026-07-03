using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gate del ciclo de vida <c>borrador → preparado</c> (N 03, RF03): validación de identidad del
/// comprador APROBADA y VIGENTE + TODOS los documentos obligatorios cargados. Reusa la misma
/// lógica de completitud que el wizard server-driven (<see cref="GetWizardStateHandler"/>) —
/// única fuente de verdad. Lo evalúa <c>TramiteLifecycleService</c> antes de aplicar la transición.
///
/// <para><b>Matrícula</b> exige: documentos completos y biométrica del comprador aprobada+vigente.</para>
/// <para><b>Traspaso</b> (HU #10459) aplica el gate COMPLETO y bloquea la preparación si falta algo:
/// documentos obligatorios completos, ambas biométricas aprobadas+vigentes, firma de compraventa de
/// comprador y vendedor, FUR generado y organismo de tránsito seleccionado.</para>
///
/// Devuelve la lista de códigos de error (vacía = puede prepararse). Códigos (contrato
/// <see cref="TramiteEstadoErrores"/>): <c>documentos_incompletos</c>, <c>identidad_no_aprobada</c>,
/// más los propios del traspaso: <c>firma_compraventa_requerida</c>, <c>fur_requerido</c>,
/// <c>organismo_requerido</c>.
/// </summary>
public static class SubmitGate
{
    public const string DocumentosIncompletos = TramiteEstadoErrores.DocumentosIncompletos;
    public const string IdentidadNoAprobada = TramiteEstadoErrores.IdentidadNoAprobada;
    public const string FirmaCompraventaRequerida = "firma_compraventa_requerida";
    public const string FurRequerido = "fur_requerido";
    public const string OrganismoRequerido = "organismo_requerido";

    /// <summary>
    /// Evalúa el gate de preparación (RF03). La instancia debe traer cargado el grafo del wizard
    /// (FieldValues, Actors, Attachments, BiometricValidations, Signatures, ChecklistEstado).
    /// </summary>
    public static IReadOnlyList<string> Evaluate(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;

        return modalidad == TramiteModalidadEntrada.Traspaso
            ? EvaluateTraspaso(instance)
            : EvaluateMatricula(instance);
    }

    private static List<string> EvaluateMatricula(ProcedureInstance instance)
    {
        var errors = new List<string>(2);

        if (!DocumentosObligatoriosCompletos(instance))
            errors.Add(DocumentosIncompletos);
        if (!BiometriaAprobada(instance, BiometricRules.ParteComprador))
            errors.Add(IdentidadNoAprobada);

        return errors;
    }

    /// <summary>
    /// Gate de traspaso (HU #10459): documentos completos + ambas biométricas + firma de compraventa
    /// de comprador y vendedor + FUR generado + organismo seleccionado. Devuelve todos los códigos
    /// incumplidos; lista vacía = puede prepararse/radicar.
    /// </summary>
    private static List<string> EvaluateTraspaso(ProcedureInstance instance)
    {
        var errors = new List<string>(5);

        if (!DocumentosObligatoriosCompletos(instance))
            errors.Add(DocumentosIncompletos);
        if (!BiometriaAprobada(instance, BiometricRules.ParteComprador)
            || !BiometriaAprobada(instance, BiometricRules.ParteVendedor))
            errors.Add(IdentidadNoAprobada);
        if (!FirmaCompraventaAmbas(instance))
            errors.Add(FirmaCompraventaRequerida);
        if (!FurGenerado(instance))
            errors.Add(FurRequerido);
        if (!OrganismoSeleccionado(instance))
            errors.Add(OrganismoRequerido);

        return errors;
    }

    // internal: reutilizado por FinalizeDraftGate (HU #10349) — misma fuente de verdad de completitud
    // documental para finalizar borrador y para preparar el trámite.
    internal static bool DocumentosObligatoriosCompletos(ProcedureInstance instance)
    {
        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var computed = ChecklistEngine.Compute(codigo, manual, docTipos);
        return computed?.Completo ?? true;
    }

    // internal: reutilizado por el wizard y el lifecycle service (RF03) — misma regla de identidad.
    internal static bool BiometriaAprobada(ProcedureInstance instance, string parte)
    {
        // HU #10350 — aprobada Y vigente (≤30 días) Y del DOCUMENTO del actor actual; una aprobación
        // vencida no prepara, y una validación de una persona anterior (documento distinto) tampoco cuenta
        // (defensa en profundidad: el gate no se fía de que el ensure del frontend haya invalidado la previa).
        var now = DateTimeOffset.UtcNow;
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
        return instance.BiometricValidations.Any(v =>
            string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigente(v, now)
            && BiometricRules.DocumentoCoincide(v, actor?.DocumentType, actor?.DocumentNumber));
    }

    internal static bool FurGenerado(ProcedureInstance instance) =>
        instance.Attachments.Any(a => string.Equals(a.Tipo, "fur", StringComparison.OrdinalIgnoreCase));

    // internal: reutilizado por FinalizeDraftGate (HU #10349).
    internal static bool OrganismoSeleccionado(ProcedureInstance instance)
    {
        var v = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_code", StringComparison.OrdinalIgnoreCase));
        return v is not null && !string.IsNullOrWhiteSpace(v.ValueText);
    }

    internal static bool FirmaCompraventaAmbas(ProcedureInstance instance)
    {
        bool Firmada(string parte) => instance.Signatures.Any(s =>
            string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase)
            && s.Estado == SignatureEstados.Firmada);

        return Firmada(SignatureRules.ParteComprador) && Firmada(SignatureRules.ParteVendedor);
    }
}
