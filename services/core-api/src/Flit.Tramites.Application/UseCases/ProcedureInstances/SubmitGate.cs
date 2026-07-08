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
/// <para><b>Traspaso unilateral</b> (HU #10592, R12) exige: documentos obligatorios completos, identidad
/// aprobada+vigente SOLO de la arrendadora (parte que transfiere, vía su representante legal — D3), FUR
/// generado y organismo seleccionado. NO exige firma de compraventa (no es compraventa directa) ni
/// impronta salvo que la tipología la incluya en su checklist. El locatario participa de forma DOCUMENTAL
/// (paz y salvo + documento del locatario), NO por validación biométrica: por eso NO entra al gate de
/// identidad.</para>
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
    public const string ImprontaRequerida = "impronta_requerida";

    /// <summary>
    /// Evalúa el gate de preparación (RF03). La instancia debe traer cargado el grafo del wizard
    /// (FieldValues, Actors, Attachments, BiometricValidations, Signatures, ChecklistEstado).
    /// </summary>
    /// <param name="documentosCompletosOverride">
    /// HU #10522 (RF17/RF22): completitud documental resuelta desde la matriz del gestor. Cuando
    /// viene (no <c>null</c>) manda sobre el cómputo interno del catálogo; <c>null</c> ⇒ se usa el
    /// gate actual (flag OFF o sin matriz) sin regresión.
    /// </param>
    public static IReadOnlyList<string> Evaluate(
        ProcedureInstance instance,
        IReadOnlySet<string> identidadAprobadaPartes,
        bool? documentosCompletosOverride = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(identidadAprobadaPartes);

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;

        var docsCompletos = documentosCompletosOverride ?? DocumentosObligatoriosCompletos(instance);

        return modalidad switch
        {
            TramiteModalidadEntrada.Traspaso => EvaluateTraspaso(instance, identidadAprobadaPartes, docsCompletos),
            TramiteModalidadEntrada.TraspasoUnilateral => EvaluateTraspasoUnilateral(instance, identidadAprobadaPartes, docsCompletos),
            _ => EvaluateMatricula(instance, identidadAprobadaPartes, docsCompletos),
        };
    }

    private static List<string> EvaluateMatricula(
        ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes, bool docsCompletos)
    {
        var errors = new List<string>(2);

        if (!docsCompletos)
            errors.Add(DocumentosIncompletos);
        // Identidad PER-PERSONA (documento del comprador), referenciada de su validación vigente (HU #10350).
        if (!identidadAprobadaPartes.Contains(BiometricRules.ParteComprador))
            errors.Add(IdentidadNoAprobada);
        if (!ImprontaGenerada(instance))
            errors.Add(ImprontaRequerida);

        return errors;
    }

    /// <summary>
    /// Gate de traspaso (HU #10459): documentos completos + ambas biométricas + firma de compraventa
    /// de comprador y vendedor + FUR generado + organismo seleccionado. Devuelve todos los códigos
    /// incumplidos; lista vacía = puede prepararse/radicar.
    /// </summary>
    private static List<string> EvaluateTraspaso(
        ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes, bool docsCompletos)
    {
        var errors = new List<string>(5);

        if (!docsCompletos)
            errors.Add(DocumentosIncompletos);
        // Identidad PER-PERSONA (documento de cada parte), referenciada de su validación vigente (HU #10350).
        if (!identidadAprobadaPartes.Contains(BiometricRules.ParteComprador)
            || !identidadAprobadaPartes.Contains(BiometricRules.ParteVendedor))
            errors.Add(IdentidadNoAprobada);
        // B12 (HU #10661, ADR-0028): la firma de compraventa NO bloquea el traspaso — negocio aún no
        // define la lógica ideal de firmas (ZapSign/baúl). Se omite el check FirmaCompraventaAmbas para
        // desbloquear preparar/radicar; el modelo y los endpoints de firma se conservan intactos.
        if (!FurGenerado(instance))
            errors.Add(FurRequerido);
        if (!OrganismoSeleccionado(instance))
            errors.Add(OrganismoRequerido);
        if (!ImprontaGenerada(instance))
            errors.Add(ImprontaRequerida);

        return errors;
    }

    /// <summary>
    /// Gate de traspaso unilateral (HU #10592, R12): documentos obligatorios completos + identidad
    /// aprobada-vigente de la ARRENDADORA (única parte que valida identidad, D3) + FUR generado + organismo
    /// seleccionado. NO exige firma de compraventa (el unilateral no es compraventa directa) ni impronta,
    /// salvo que la tipología la incluya en su checklist (<see cref="ImprontaGenerada"/> se autoexime cuando
    /// la tipología no la lista). El LOCATARIO es documental: NO se comprueba su identidad. Devuelve todos
    /// los códigos incumplidos; lista vacía = puede prepararse/radicar.
    /// </summary>
    private static List<string> EvaluateTraspasoUnilateral(
        ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes, bool docsCompletos)
    {
        var errors = new List<string>(4);

        if (!docsCompletos)
            errors.Add(DocumentosIncompletos);
        // Identidad PER-PERSONA de la ARRENDADORA (rep. legal). El locatario NO valida identidad (documental).
        if (!identidadAprobadaPartes.Contains(BiometricRules.ParteArrendadora))
            errors.Add(IdentidadNoAprobada);
        if (!FurGenerado(instance))
            errors.Add(FurRequerido);
        if (!OrganismoSeleccionado(instance))
            errors.Add(OrganismoRequerido);
        // Sin firma de compraventa. Impronta solo si el checklist de la tipología unilateral la incluye
        // (hoy no la incluye → ImprontaGenerada devuelve true y no bloquea).
        if (!ImprontaGenerada(instance))
            errors.Add(ImprontaRequerida);

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

    /// <summary>
    /// ¿La tipología del trámite tiene un ítem de checklist con <c>DocTipo == "impronta"</c>? Así una
    /// tipología futura sin impronta en su checklist no queda bloqueada por este gate.
    /// </summary>
    private static bool RequiereImpronta(ProcedureInstance instance)
    {
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var tip = TramiteTipologiaCatalog.Get(codigo);
        return tip is not null && tip.Checklist.Any(i => string.Equals(i.DocTipo, "impronta", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ImprontaGenerada(ProcedureInstance instance) =>
        !RequiereImpronta(instance)
        || instance.Attachments.Any(a => string.Equals(a.Tipo, "impronta", StringComparison.OrdinalIgnoreCase));
}
