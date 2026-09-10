using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Fuente ÚNICA del criterio "identidad vigente por documento" sobre
/// <c>tramites.procedure_instance_biometric_validations</c> (Bug #11583, defectos 1 y 2). Antes del fix,
/// cada consumidor (<see cref="RepresentativeIdentityLookup"/>, <c>ProcedureInstanceRepository</c>,
/// <see cref="PersonIdentityLookup"/>) reimplementaba a mano el empate por documento y la ventana de
/// vigencia, y solo uno de ellos (<c>ProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync</c>,
/// HU #10867) incluía las prevalidaciones standalone — la divergencia dejaba identidades vigentes
/// "invisibles" según qué consumidor preguntara. Estos tres extension methods son el ÚNICO lugar donde se
/// arma cada cláusula; los repositorios los componen, no la reescriben.
/// </summary>
internal static class BiometricDocumentMatchQuery
{
    /// <summary>
    /// Empate de documento normalizado (Trim + Upper, <see cref="DocumentCanonicalNormalization"/>) —
    /// misma regla que el índice funcional <c>ix_biometric_validations_doc_norm_created</c>
    /// (<c>upper(btrim(document_type))</c> / <c>upper(btrim(document_number))</c>, HU #11269), así la
    /// normalización en SQL no invalida ese índice. Antes de este fix, <c>RepresentativeIdentityLookup</c>
    /// y <c>FindVigenteApprovedByDocumentAsync</c> comparaban con igualdad exacta: una validación con
    /// <c>document_type = "cc"</c> o <c>" Cc"</c> no empataba contra el <c>"CC"</c> canónico del
    /// representante legal, aunque fuera la MISMA persona (defecto 1).
    /// </summary>
    public static IQueryable<ProcedureInstanceBiometricValidation> WhereDocumentoVigenteCandidato(
        this IQueryable<ProcedureInstanceBiometricValidation> query,
        string? documentType,
        string? documentNumber)
    {
        var (tipo, numero) = DocumentCanonicalNormalization.Normalize(documentType, documentNumber);
        return query.Where(v => v.DocumentType.Trim().ToUpper() == tipo
            && v.DocumentNumber.Trim().ToUpper() == numero);
    }

    /// <summary>
    /// Ventana de vigencia: filtro grueso en SQL por <c>ValidUntil</c> (fuente de verdad cuando está
    /// estampado) o, a falta de él, por <c>ValidatedAt</c> con un día de margen. El corte fino por DÍA
    /// calendario lo sigue aplicando <see cref="BiometricRules.EsAprobadaVigente"/> en memoria sobre los
    /// candidatos ya traídos — este método NO reemplaza esa verificación.
    /// </summary>
    public static IQueryable<ProcedureInstanceBiometricValidation> WhereVentanaVigencia(
        this IQueryable<ProcedureInstanceBiometricValidation> query,
        DateTimeOffset now)
    {
        var cutoff = now.AddDays(-(BiometricRules.VigenciaDias + 1));
        return query.Where(v => (v.ValidUntil != null && v.ValidUntil > now)
            || (v.ValidUntil == null && v.ValidatedAt != null && v.ValidatedAt >= cutoff));
    }

    /// <summary>
    /// HU #10867 — incluye las prevalidaciones STANDALONE (sin trámite, <c>ProcedureInstanceId == null</c>)
    /// además de las ligadas a una instancia no eliminada. <c>RepresentativeIdentityLookup</c> exigía
    /// <c>ProcedureInstance != null</c> (defecto 2, Bug #11583): una identidad validada FUERA de un
    /// trámite quedaba invisible para el resolutor de representante legal aunque
    /// <c>FindVigenteApprovedByDocumentAsync</c> (comprador/vendedor) ya la viera.
    /// </summary>
    public static IQueryable<ProcedureInstanceBiometricValidation> WhereInstanciaVigente(
        this IQueryable<ProcedureInstanceBiometricValidation> query) =>
        query.Where(v => v.ProcedureInstanceId == null
            || (v.ProcedureInstance != null && v.ProcedureInstance.DeletedAt == null));
}
