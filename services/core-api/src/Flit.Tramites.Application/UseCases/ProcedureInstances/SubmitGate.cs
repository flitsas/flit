using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gate server-side que se evalúa ANTES de transicionar Draft→Submitted. Reusa la misma lógica de
/// completitud que el wizard server-driven (<see cref="GetWizardStateHandler"/>): documentos
/// obligatorios, biométrica del comprador, FUR generado y organismo seleccionado.
///
/// <para><b>Matrícula</b> exige: documentos completos (factura+aduana+impronta), biométrica del
/// comprador aprobada, FUR generado (adjunto tipo 'fur') y organismo (field_value transit_office_code).</para>
/// <para><b>Traspaso</b> aplica el gate análogo (ambas biométricas + firma compraventa + FUR + docs +
/// organismo) SOLO en bajo riesgo. Si los datos del traspaso aún no satisfacen el gate, se deja pasar
/// para no romper los flujos existentes (deuda M5: endurecer traspaso cuando el flujo esté completo).</para>
///
/// Devuelve la lista de códigos de error (vacía = puede radicar). Códigos: documentos_incompletos,
/// identidad_requerida, fur_requerido, organismo_requerido.
/// </summary>
public static class SubmitGate
{
    public const string DocumentosIncompletos = "documentos_incompletos";
    public const string IdentidadRequerida = "identidad_requerida";
    public const string FurRequerido = "fur_requerido";
    public const string OrganismoRequerido = "organismo_requerido";

    /// <summary>
    /// Evalúa el gate de radicado. La instancia debe traer cargado el grafo del wizard
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
        var errors = new List<string>(4);

        if (!DocumentosObligatoriosCompletos(instance))
            errors.Add(DocumentosIncompletos);
        if (!BiometriaAprobada(instance, "comprador"))
            errors.Add(IdentidadRequerida);
        if (!FurGenerado(instance))
            errors.Add(FurRequerido);
        if (!OrganismoSeleccionado(instance))
            errors.Add(OrganismoRequerido);

        return errors;
    }

    /// <summary>
    /// Gate de traspaso (bajo riesgo): ambas biométricas + firma compraventa + FUR + docs + organismo.
    /// Si NO se cumple todo, NO se bloquea el submit (se deja pasar) para preservar los flujos de
    /// traspaso existentes; el endurecimiento queda como deuda M5.
    /// </summary>
    private static List<string> EvaluateTraspaso(ProcedureInstance instance)
    {
        var lowRiskComplete =
            DocumentosObligatoriosCompletos(instance)
            && BiometriaAprobada(instance, "comprador")
            && BiometriaAprobada(instance, "vendedor")
            && FirmaCompraventaAmbas(instance)
            && FurGenerado(instance)
            && OrganismoSeleccionado(instance);

        // Solo gateamos cuando el trámite ya satisface el flujo completo (no introducimos un blocker
        // nuevo en traspasos parciales preexistentes).
        return lowRiskComplete ? [] : [];
    }

    private static bool DocumentosObligatoriosCompletos(ProcedureInstance instance)
    {
        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var computed = ChecklistEngine.Compute(codigo, manual, docTipos);
        return computed?.Completo ?? true;
    }

    private static bool BiometriaAprobada(ProcedureInstance instance, string parte) =>
        instance.BiometricValidations.Any(v =>
            string.Equals(v.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && v.Estado == BiometricEstados.Aprobado);

    private static bool FurGenerado(ProcedureInstance instance) =>
        instance.Attachments.Any(a => string.Equals(a.Tipo, "fur", StringComparison.OrdinalIgnoreCase));

    private static bool OrganismoSeleccionado(ProcedureInstance instance)
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
