using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gate server-side que se evalúa ANTES de transicionar Draft→Submitted. Reusa la misma lógica de
/// completitud que el wizard server-driven (<see cref="GetWizardStateHandler"/>): documentos
/// obligatorios y biométrica del comprador. El FUR y el expediente consolidado son opcionales en
/// radicación y pueden generarse a posteriori vía POST /fur y POST /consolidado.
///
/// <para><b>Matrícula</b> exige: documentos completos (factura+aduana+impronta) y biométrica del
/// comprador aprobada. FUR, consolidado y organismo de tránsito NO bloquean el envío.</para>
/// <para><b>Traspaso</b> (HU #10459) aplica el gate COMPLETO y bloquea la radicación si falta algo:
/// documentos obligatorios completos, ambas biométricas aprobadas+vigentes, firma de compraventa de
/// comprador y vendedor, FUR generado y organismo de tránsito seleccionado.</para>
///
/// Devuelve la lista de códigos de error (vacía = puede radicar). Códigos: documentos_incompletos,
/// identidad_requerida, firma_compraventa_requerida, fur_requerido, organismo_requerido.
/// </summary>
public static class SubmitGate
{
    public const string DocumentosIncompletos = "documentos_incompletos";
    public const string IdentidadRequerida = "identidad_requerida";
    public const string FirmaCompraventaRequerida = "firma_compraventa_requerida";
    public const string FurRequerido = "fur_requerido";
    public const string OrganismoRequerido = "organismo_requerido";

    /// <summary>
    /// Evalúa el gate de radicado. La instancia debe traer cargado el grafo del wizard
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
        var errors = new List<string>(4);

        if (!DocumentosObligatoriosCompletos(instance))
            errors.Add(DocumentosIncompletos);
        // Identidad PER-PERSONA (documento del comprador), referenciada de su validación vigente (HU #10350).
        if (!identidadAprobadaPartes.Contains("comprador"))
            errors.Add(IdentidadRequerida);

        return errors;
    }

    /// <summary>
    /// Gate de traspaso (HU #10459): documentos completos + ambas biométricas + firma de compraventa
    /// de comprador y vendedor + FUR generado + organismo seleccionado. Devuelve todos los códigos
    /// incumplidos; lista vacía = puede radicar.
    /// </summary>
    private static List<string> EvaluateTraspaso(ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes)
    {
        var errors = new List<string>(5);

        if (!DocumentosObligatoriosCompletos(instance))
            errors.Add(DocumentosIncompletos);
        // Identidad PER-PERSONA (documento de cada parte), referenciada de su validación vigente (HU #10350).
        if (!identidadAprobadaPartes.Contains("comprador") || !identidadAprobadaPartes.Contains("vendedor"))
            errors.Add(IdentidadRequerida);
        if (!FirmaCompraventaAmbas(instance))
            errors.Add(FirmaCompraventaRequerida);
        if (!FurGenerado(instance))
            errors.Add(FurRequerido);
        if (!OrganismoSeleccionado(instance))
            errors.Add(OrganismoRequerido);

        return errors;
    }

    // internal: reutilizado por FinalizeDraftGate (HU #10349) — misma fuente de verdad de completitud
    // documental para finalizar borrador y para radicar.
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

    private static bool FirmaCompraventaAmbas(ProcedureInstance instance)
    {
        bool Firmada(string parte) => instance.Signatures.Any(s =>
            string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase)
            && s.Estado == SignatureEstados.Firmada);

        return Firmada(SignatureRules.ParteComprador) && Firmada(SignatureRules.ParteVendedor);
    }
}
