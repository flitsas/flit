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
    public static IReadOnlyList<string> Evaluate(ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(identidadAprobadaPartes);

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;

        return modalidad == TramiteModalidadEntrada.Traspaso
            ? EvaluateTraspaso(instance, identidadAprobadaPartes)
            : EvaluateMatricula(instance, identidadAprobadaPartes);
    }

    private static List<string> EvaluateMatricula(ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes)
    {
        var errors = new List<string>(2);

        if (!DocumentosObligatoriosCompletos(instance))
            errors.Add(DocumentosIncompletos);
        // Identidad PER-PERSONA (documento del comprador), referenciada de su validación vigente (HU #10350).
        if (!identidadAprobadaPartes.Contains(BiometricRules.ParteComprador))
            errors.Add(IdentidadNoAprobada);

        return errors;
    }

    /// <summary>
    /// Gate de traspaso (HU #10459): documentos completos + ambas biométricas + firma de compraventa
    /// de comprador y vendedor + FUR generado + organismo seleccionado. Devuelve todos los códigos
    /// incumplidos; lista vacía = puede prepararse/radicar.
    /// </summary>
    private static List<string> EvaluateTraspaso(ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes)
    {
        var errors = new List<string>(5);

        if (!DocumentosObligatoriosCompletos(instance))
            errors.Add(DocumentosIncompletos);
        // Identidad PER-PERSONA (documento de cada parte), referenciada de su validación vigente (HU #10350).
        if (!identidadAprobadaPartes.Contains(BiometricRules.ParteComprador)
            || !identidadAprobadaPartes.Contains(BiometricRules.ParteVendedor))
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

    private static bool FurGenerado(ProcedureInstance instance) =>
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
