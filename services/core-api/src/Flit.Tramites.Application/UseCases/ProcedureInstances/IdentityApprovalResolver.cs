using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Resuelve qué partes de un trámite tienen la identidad APROBADA Y VIGENTE. Las partes que llevan
/// validación de identidad dependen de la MODALIDAD del trámite (ver <see cref="PartesParaModalidad"/>):
/// matrícula → <c>[comprador]</c>; traspaso → <c>[comprador, vendedor]</c>; traspaso unilateral →
/// <c>[arrendadora]</c> (el locatario es documental, HU #10592). Es
/// HÍBRIDO: cuenta la validación PROPIA del trámite (fila local, como antes) O —si no la hay— la identidad
/// vigente de la PERSONA (documento del actor) en otro trámite del tenant, referenciándola SIN clonar
/// (HU #10350 rediseño: una persona valida una sola vez y sirve para N trámites hasta que venza). Fuente de
/// verdad cross-trámite = <see cref="IProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync"/>.
/// </summary>
internal static class IdentityApprovalResolver
{
    /// <summary>
    /// Partes que llevan validación de identidad según la MODALIDAD de entrada del trámite:
    /// <list type="bullet">
    /// <item><c>matricula_inicial</c> → <c>[comprador]</c> (adquirente único).</item>
    /// <item><c>traspaso</c> → <c>[comprador, vendedor]</c> (ambas partes validan).</item>
    /// <item><c>traspaso_unilateral</c> → <c>[arrendadora]</c> (solo la parte que transfiere, vía rep.
    /// legal; el locatario es documental — HU #10592, D3).</item>
    /// </list>
    /// Cualquier modalidad no reconocida cae al default de matrícula (<c>[comprador]</c>), consistente con
    /// <see cref="SubmitGate.Evaluate"/>.
    /// </summary>
    private static IReadOnlyList<string> PartesParaModalidad(string? modalidadEntrada) =>
        TramiteModalidadEntradaCodes.FromCode(modalidadEntrada) switch
        {
            TramiteModalidadEntrada.Traspaso => [BiometricRules.ParteComprador, BiometricRules.ParteVendedor],
            TramiteModalidadEntrada.TraspasoUnilateral => [BiometricRules.ParteArrendadora],
            _ => [BiometricRules.ParteComprador],
        };

    /// <summary>
    /// Partes con identidad vigente aprobada, resueltas por CONSULTA directa (una instancia). Fila propia →
    /// en memoria; si no hay, hasta 2 lecturas al repo (comprador/vendedor). El LISTADO usa
    /// <see cref="ApprovedPartiesFromKeys"/> (claves precomputadas en lote, sin N+1).
    /// </summary>
    public static async Task<IReadOnlySet<string>> ResolveApprovedPartiesAsync(
        IProcedureInstanceRepository repo, ProcedureInstance instance, DateTimeOffset now, CancellationToken ct)
    {
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parte in PartesParaModalidad(instance.ModalidadEntrada))
        {
            var (tipoDoc, documento) = ActorDoc(instance, parte);

            // 1) Fila PROPIA del trámite (aprobada+vigente+documento del actor): validó EN este trámite.
            if (HasLocalVigente(instance, parte, tipoDoc, documento, now))
            {
                approved.Add(parte);
                continue;
            }

            // 2) Sin fila propia → se REFERENCIA la identidad vigente de la PERSONA (documento) en otro trámite
            // del tenant, sin clonar. Requiere documento del actor.
            if (string.IsNullOrWhiteSpace(tipoDoc) || string.IsNullOrWhiteSpace(documento))
                continue;

            var vigente = await repo.FindVigenteApprovedByDocumentAsync(
                instance.TenantId, tipoDoc.Trim(), documento.Trim(), now, ct);
            if (vigente is not null)
                approved.Add(parte);
        }

        return approved;
    }

    /// <summary>
    /// Partes con identidad vigente aprobada a partir de un set de CLAVES ya materializado
    /// (<see cref="BiometricRules.IdentidadKey"/>) MÁS la fila propia del trámite. Puro y sin E/S: lo usa el
    /// listado, que precomputa las claves del tenant en UNA consulta (evita N+1). Las claves ya incluyen las
    /// filas propias del tenant, pero el fallback local mantiene consistencia con dobles/mocks.
    /// </summary>
    public static IReadOnlySet<string> ApprovedPartiesFromKeys(
        ProcedureInstance instance, IReadOnlySet<string> approvedKeys, DateTimeOffset now)
    {
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parte in PartesParaModalidad(instance.ModalidadEntrada))
        {
            var (tipoDoc, documento) = ActorDoc(instance, parte);

            if (HasLocalVigente(instance, parte, tipoDoc, documento, now))
            {
                approved.Add(parte);
                continue;
            }

            if (string.IsNullOrWhiteSpace(tipoDoc) || string.IsNullOrWhiteSpace(documento))
                continue;

            if (approvedKeys.Contains(BiometricRules.IdentidadKey(instance.TenantId, tipoDoc, documento)))
                approved.Add(parte);
        }

        return approved;
    }

    /// <summary>Tipo+número de documento del actor de la parte (nulls si no hay actor o le falta documento).</summary>
    private static (string? TipoDoc, string? Documento) ActorDoc(ProcedureInstance instance, string parte)
    {
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
        return (actor?.DocumentType, actor?.DocumentNumber);
    }

    /// <summary>¿El trámite tiene una validación PROPIA de la parte aprobada+vigente y del documento del actor?</summary>
    private static bool HasLocalVigente(
        ProcedureInstance instance, string parte, string? tipoDoc, string? documento, DateTimeOffset now) =>
        instance.BiometricValidations.Any(v =>
            string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigente(v, now)
            && BiometricRules.DocumentoCoincide(v, tipoDoc, documento));
}
