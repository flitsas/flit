using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Resuelve qué partes (comprador/vendedor) de un trámite tienen la identidad APROBADA Y VIGENTE. Es
/// HÍBRIDO: cuenta la validación PROPIA del trámite (fila local, como antes) O —si no la hay— la identidad
/// vigente de la PERSONA (documento del actor) en otro trámite del tenant, referenciándola SIN clonar
/// (HU #10350 rediseño: una persona valida una sola vez y sirve para N trámites hasta que venza). Fuente de
/// verdad cross-trámite = <see cref="IProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync"/>.
/// </summary>
internal static class IdentityApprovalResolver
{
    /// <summary>Partes que llevan validación de identidad (matrícula usa solo comprador).</summary>
    private static readonly string[] Partes = ["comprador", "vendedor"];

    /// <summary>
    /// Partes con identidad vigente aprobada, resueltas por CONSULTA directa (una instancia). Fila propia →
    /// en memoria; si no hay, hasta 2 lecturas al repo (comprador/vendedor). El LISTADO usa
    /// <see cref="ApprovedPartiesFromKeys"/> (claves precomputadas en lote, sin N+1).
    /// </summary>
    public static async Task<IReadOnlySet<string>> ResolveApprovedPartiesAsync(
        IProcedureInstanceRepository repo, ProcedureInstance instance, DateTimeOffset now, CancellationToken ct,
        ISignatureVaultPolicy? vaultPolicy = null)
    {
        var vault = vaultPolicy ?? NullSignatureVaultPolicy.Instance;
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parte in Partes)
        {
            var actor = ActorFor(instance, parte);
            var (tipoDoc, documento) = ActorDoc(actor);

            // 0) BAÚL DE FIRMAS (ADR-0025 §4, HU #10645, R14): un actor JURÍDICO cubierto por una firma de
            // baúl ACTIVA+VIGENTE cuenta como identidad APROBADA — así el SubmitGate y el gate del FUR lo
            // tratan como validado sin exigir biométrica. Precedencia D8: el baúl va PRIMERO. HU #10930/#10937:
            // la firma se resuelve por el documento del REPRESENTANTE LEGAL seleccionado (el sujeto de
            // identidad = tipoDoc/documento), no por el NIT. Solo actores jurídicos; las personas naturales
            // caen a los pasos 1/2. Null-safe: sin baúl habilitado devuelve null.
            if (actor is not null && EsActorJuridico(actor.DocumentType)
                && !string.IsNullOrWhiteSpace(tipoDoc) && !string.IsNullOrWhiteSpace(documento)
                && await vault.ResolveAsync(instance.TenantId, tipoDoc.Trim(), documento.Trim(), ct) is not null)
            {
                approved.Add(parte);
                continue;
            }

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
    /// <para><b>Baúl de firmas (HU #10645):</b> esta ruta de LOTE NO consulta el baúl. Resolver la firma de
    /// baúl por parte exige una llamada asíncrona a <see cref="ISignatureVaultPolicy"/> por (tenant, NIT)
    /// que no se puede precomputar barato desde las claves de identidad biométrica (romería N+1 en el
    /// listado). Los chips del listado son informativos; los gates que SÍ importan (SubmitGate vía
    /// <c>TramiteLifecycleService</c> y el gate del FUR) usan la ruta per-instancia
    /// <see cref="ResolveApprovedPartiesAsync"/>, que sí resuelve el baúl.</para>
    /// </summary>
    public static IReadOnlySet<string> ApprovedPartiesFromKeys(
        ProcedureInstance instance, IReadOnlySet<string> approvedKeys, DateTimeOffset now)
    {
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parte in Partes)
        {
            var (tipoDoc, documento) = ActorDoc(ActorFor(instance, parte));

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

    /// <summary>¿El actor es persona JURÍDICA (NIT/N)? Solo estos consumen el baúl de firmas (ADR-0025 §4).</summary>
    private static bool EsActorJuridico(string? documentType)
    {
        var t = documentType?.Trim();
        return string.Equals(t, "NIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "N", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Actor de la parte (comprador/vendedor) del trámite, o <c>null</c> si no existe.</summary>
    private static ProcedureInstanceActor? ActorFor(ProcedureInstance instance, string parte) =>
        instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Tipo+número de documento del SUJETO de identidad de la parte (HU #10688): el actor si es natural, el
    /// representante legal seleccionado si es jurídico. Nulls si no hay actor o al sujeto le falta documento.
    /// </summary>
    private static (string? TipoDoc, string? Documento) ActorDoc(ProcedureInstanceActor? actor)
    {
        if (actor is null)
            return (null, null);
        var subject = IdentitySubjectResolver.For(actor);
        return (subject.TipoDocumento, subject.NumeroDocumento);
    }

    /// <summary>¿El trámite tiene una validación PROPIA de la parte aprobada+vigente y del documento del actor?</summary>
    private static bool HasLocalVigente(
        ProcedureInstance instance, string parte, string? tipoDoc, string? documento, DateTimeOffset now) =>
        instance.BiometricValidations.Any(v =>
            string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigente(v, now)
            && BiometricRules.DocumentoCoincide(v, tipoDoc, documento));
}
