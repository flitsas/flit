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
    /// <remarks>
    /// ADR-0053 (Múltiple Propietario) — "todos firman": la parte entra al set <c>approved</c> SOLO SI
    /// TODOS sus actores (<see cref="ActoresDe"/>, 1..4, <c>OrderBy(Ordinal)</c>) pasan la MISMA
    /// comprobación de hoy (baúl → fila propia vigente → identidad referenciada). Con un solo actor
    /// por parte (caso mayoritario, sin cambios de UX) el bucle interno itera una sola vez y el
    /// comportamiento es idéntico byte a byte al anterior a esta versión (que resolvía un único
    /// <c>ActorFor</c>). Sin ningún actor para la parte, sigue sin aprobarse (comportamiento previo).
    /// </remarks>
    public static async Task<IReadOnlySet<string>> ResolveApprovedPartiesAsync(
        IProcedureInstanceRepository repo, ProcedureInstance instance, DateTimeOffset now, CancellationToken ct,
        ISignatureVaultPolicy? vaultPolicy = null)
    {
        // Trámite ya radicado: el baúl se lee de la COPIA tomada al radicar, nunca en vivo (FirmaBaulCopias).
        // Borrador / anterior a la mejora: baúl en vivo, como siempre.
        var vault = await FirmaBaulCopias
            .EfectivaAsync(repo, vaultPolicy ?? NullSignatureVaultPolicy.Instance, instance, ct)
            .ConfigureAwait(false);
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // HU #13824 - la salida de borrador solo se consulta si hace falta (una fila propia vencida a mano) o, mas
        // abajo, al caer al fallback de identidad vencida a mano de otro tramite.
        var salidaCargada = TieneVencidaManualLocal(instance);
        DateTimeOffset? salidaDeBorrador = salidaCargada
            ? await repo.GetSalidaDeBorradorAtAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false)
            : null;
        foreach (var parte in Partes)
        {
            // Bug #13194 (P4, D4) — fail-closed: una parte SIN actor no queda aprobada. Antes se evaluaba
            // con `actor = null` y `DocumentoCoincide` hacía match abierto, así que cualquier validación
            // aprobada del ROL —de otra persona del mismo tenant— aprobaba la parte. Sin sujeto con
            // documento no se puede afirmar de quién es la identidad.
            var actores = ActoresDe(instance, parte);
            if (actores.Count == 0)
                continue;

            var todosCubiertos = true;
            foreach (var actor in actores)
            {
                var (tipoDoc, documento) = ActorDoc(actor);

                // 0) BAÚL DE FIRMAS (ADR-0025 §4, HU #10645, R14): un actor JURÍDICO cubierto por una firma de
                // baúl ACTIVA+VIGENTE cuenta como identidad APROBADA — así el SubmitGate y el gate del FUR lo
                // tratan como validado sin exigir biométrica. Precedencia D8: el baúl va PRIMERO. HU #10930/#10937:
                // la firma se resuelve por el documento del REPRESENTANTE LEGAL seleccionado (el sujeto de
                // identidad = tipoDoc/documento), no por el NIT. Solo actores jurídicos; las personas naturales
                // caen a los pasos 1/2. Null-safe: sin baúl habilitado devuelve null.
                // HU #11661/#11660: el predicado es UNO —FirmaBaulCobertura.Aplica— y además del tipo de
                // documento tiene en cuenta el MECANISMO DE FIRMA elegido por el gestor (HU #11061). Sin
                // esa segunda mitad, una parte con «sello de validación de identidad» seleccionado y firma
                // de baúl vigente se daba por aprobada aquí, y el trámite se radicaba sin que la biométrica
                // se hubiera hecho: el documento se firmaba con un sello que no existía. ADR-0039 prescribe
                // literalmente este cambio y nombra el Bug #11141 como causa.
                if (FirmaBaulCobertura.Aplica(actor)
                    && !string.IsNullOrWhiteSpace(tipoDoc) && !string.IsNullOrWhiteSpace(documento)
                    && await vault.ResolveAsync(instance.TenantId, tipoDoc.Trim(), documento.Trim(), ct) is not null)
                    continue; // este actor queda cubierto por el baúl.

                // 1) Fila PROPIA del trámite (aprobada+vigente+documento del actor): validó EN este trámite.
                if (HasLocalVigente(instance, parte, tipoDoc, documento, now, salidaDeBorrador))
                    continue; // este actor queda cubierto por su fila propia.

                // 2) Sin fila propia → se REFERENCIA la identidad vigente de la PERSONA (documento) en otro
                // trámite del tenant, sin clonar. Requiere documento del actor.
                if (string.IsNullOrWhiteSpace(tipoDoc) || string.IsNullOrWhiteSpace(documento))
                {
                    todosCubiertos = false;
                    break; // este actor no tiene ni siquiera documento: la parte no queda aprobada.
                }

                var vigente = await repo.FindVigenteApprovedByDocumentAsync(
                    instance.TenantId, tipoDoc.Trim(), documento.Trim(), now, ct);
                // HU #13824 — un trámite que ya NO está en borrador conserva su firma aunque un Super Admin haya vencido
                // a mano la identidad de la persona (hasta el fin de vigencia que tenía). Un borrador no la conserva.
                if (vigente is null && !EsBorrador(instance))
                {
                    if (!salidaCargada)
                    {
                        salidaDeBorrador = await repo.GetSalidaDeBorradorAtAsync(instance.Id, instance.TenantId, ct)
                            .ConfigureAwait(false);
                        salidaCargada = true;
                    }

                    vigente = await repo.FindProtegidaPorVencimientoManualAsync(
                        instance.TenantId, tipoDoc.Trim(), documento.Trim(), now, salidaDeBorrador, ct);
                }
                if (vigente is null)
                {
                    todosCubiertos = false;
                    break; // este actor no está cubierto por ningún mecanismo: la parte no queda aprobada.
                }
            }

            if (todosCubiertos)
                approved.Add(parte);
        }

        return approved;
    }

    /// <summary>
    /// Partes con identidad vigente aprobada a partir de sets de CLAVES ya materializados
    /// (<see cref="BiometricRules.IdentidadKey"/>) MÁS la fila propia del trámite. Puro y sin E/S: lo usa el
    /// listado, que precomputa las claves del tenant en UNA consulta (evita N+1). Las claves ya incluyen las
    /// filas propias del tenant, pero el fallback local mantiene consistencia con dobles/mocks.
    ///
    /// <para><b>Baúl de firmas (HU #11667).</b> Esta ruta SÍ acredita por baúl, con la misma precedencia
    /// que la per-instancia (el baúl primero, ADR-0025 D8) y respetando
    /// <see cref="FirmaBaulCobertura.Aplica"/> —es decir, el mecanismo de firma elegido por el gestor—.
    /// Antes no lo hacía y el chip del listado podía contradecir al gate de radicación y al FUR: una
    /// parte jurídica que firma desde el baúl salía sin identidad aunque el trámite se radicara.
    /// <b>No cuesta ninguna consulta nueva:</b> el listado ya materializa las vigencias del baúl en UNA
    /// sola consulta para todos los tenants (<c>ListFirmaBaulVigenciaKeysAsync</c>) y con la MISMA llave;
    /// solo faltaba pasárselas. El comentario anterior —que justificaba la omisión con un N+1— quedó
    /// obsoleto cuando esa consulta entró para la columna «Firmado».</para>
    ///
    /// <para><b>Lo que sigue sin cubrirse.</b> (1) Solo se acredita por estados terminales de la
    /// identidad: los no terminales (<c>en_proceso</c>, <c>rechazado</c>) siguen leyéndose únicamente de
    /// las filas PROPIAS del trámite, porque las claves en lote solo traen identidades aprobadas y
    /// vigentes. (2) Bug #13194 (P4, D1): <c>firmaBaulVigentePorPersona</c> ya solo trae firmas de
    /// tenants con <c>signature_vault_enabled</c> activo —el filtro se aplica en el origen de las claves
    /// (<c>ListFirmaBaulVigenciaKeysAsync</c>), en la misma consulta—, igual que la ruta per-instancia vía
    /// <see cref="ISignatureVaultPolicy"/>. Antes la asimetría hacía que el listado pintara «Firmado»
    /// mientras el paso de identidad y el gate decían «no iniciada».</para>
    /// </summary>
    /// <remarks>
    /// ADR-0053 (Múltiple Propietario) — misma extensión "todos los actores" que
    /// <see cref="ResolveApprovedPartiesAsync"/>, adaptada al camino de claves precomputadas del
    /// listado (sin E/S por actor). Con un solo actor por parte, cero regresión.
    /// </remarks>
    public static IReadOnlySet<string> ApprovedPartiesFromKeys(
        ProcedureInstance instance,
        IReadOnlySet<string> approvedKeys,
        DateTimeOffset now,
        IReadOnlyDictionary<string, bool>? firmaBaulVigentePorPersona = null,
        IReadOnlyDictionary<string, IReadOnlyList<IdentidadVencidaManual>>? protegidasKeys = null,
        DateTimeOffset? salidaDeBorradorAt = null)
    {
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parte in Partes)
        {
            // Bug #13194 (P4, D4) — misma regla fail-closed que ResolveApprovedPartiesAsync: sin actor,
            // la parte no queda aprobada.
            var actores = ActoresDe(instance, parte);
            if (actores.Count == 0)
                continue;

            var todosCubiertos = actores.TrueForAll(actor => ActorCubiertoPorClaves(
                instance, parte, actor, approvedKeys, now, firmaBaulVigentePorPersona, protegidasKeys, salidaDeBorradorAt));

            if (todosCubiertos)
                approved.Add(parte);
        }

        return approved;
    }

    /// <summary>
    /// ADR-0053 — actores (de cualquier lado) con identidad acreditada, uno por uno, con EXACTAMENTE la
    /// comprobación de <see cref="ApprovedPartiesFromKeys"/>. Lo usa el desplegable de copropietarios
    /// del listado para pintar la firma de cada persona; el set por parte sigue siendo «todos firman».
    /// Comparación por referencia: los actores son los mismos objetos del grafo cargado.
    /// </summary>
    public static IReadOnlySet<ProcedureInstanceActor> ApprovedActorsFromKeys(
        ProcedureInstance instance,
        IReadOnlySet<string> approvedKeys,
        DateTimeOffset now,
        IReadOnlyDictionary<string, bool>? firmaBaulVigentePorPersona = null,
        IReadOnlyDictionary<string, IReadOnlyList<IdentidadVencidaManual>>? protegidasKeys = null,
        DateTimeOffset? salidaDeBorradorAt = null)
    {
        var cubiertos = new HashSet<ProcedureInstanceActor>(ReferenceEqualityComparer.Instance);
        foreach (var parte in Partes)
        {
            foreach (var actor in ActoresDe(instance, parte))
            {
                if (ActorCubiertoPorClaves(
                        instance, parte, actor, approvedKeys, now, firmaBaulVigentePorPersona, protegidasKeys, salidaDeBorradorAt))
                    cubiertos.Add(actor);
            }
        }

        return cubiertos;
    }

    /// <summary>
    /// ¿Este actor tiene identidad acreditada, según las claves precomputadas del listado? Orden: baúl →
    /// fila propia vigente → identidad referenciada (o protegida tras vencimiento manual, HU #13824).
    /// </summary>
    private static bool ActorCubiertoPorClaves(
        ProcedureInstance instance,
        string parte,
        ProcedureInstanceActor actor,
        IReadOnlySet<string> approvedKeys,
        DateTimeOffset now,
        IReadOnlyDictionary<string, bool>? firmaBaulVigentePorPersona,
        IReadOnlyDictionary<string, IReadOnlyList<IdentidadVencidaManual>>? protegidasKeys,
        DateTimeOffset? salidaDeBorradorAt)
    {
        var (tipoDoc, documento) = ActorDoc(actor);

        // 0) BAÚL — mismo orden y mismo predicado que ResolveApprovedPartiesAsync.
        if (firmaBaulVigentePorPersona is not null
            && FirmaBaulCobertura.Aplica(actor)
            && !string.IsNullOrWhiteSpace(tipoDoc) && !string.IsNullOrWhiteSpace(documento)
            && firmaBaulVigentePorPersona.TryGetValue(
                BiometricRules.IdentidadKey(instance.TenantId, tipoDoc, documento), out var baulVigente)
            && baulVigente)
            return true;

        if (HasLocalVigente(instance, parte, tipoDoc, documento, now, salidaDeBorradorAt))
            return true;

        if (string.IsNullOrWhiteSpace(tipoDoc) || string.IsNullOrWhiteSpace(documento))
            return false;

        var key = BiometricRules.IdentidadKey(instance.TenantId, tipoDoc, documento);
        return approvedKeys.Contains(key)
            // HU #13824 — ver ResolveApprovedPartiesAsync: fuera de borrador se conserva la firma.
            || (protegidasKeys is not null
                && protegidasKeys.TryGetValue(key, out var vencidas)
                && BiometricRules.ConservaFirmaTrasVencimientoManual(instance.Status, salidaDeBorradorAt, vencidas));
    }

    /// <summary>
    /// Todos los actores de la parte (comprador/vendedor), 1..4, en orden de <c>Ordinal</c> — ADR-0053
    /// (Múltiple Propietario). Reemplaza al antiguo <c>ActorFor</c> (un solo <c>FirstOrDefault</c>):
    /// "todos firman" exige recorrer cada copropietario del lado, no solo el principal.
    /// </summary>
    private static List<ProcedureInstanceActor> ActoresDe(ProcedureInstance instance, string parte) =>
        instance.Actors
            .Where(a => string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Ordinal)
            .ToList();

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

    /// <summary>HU #13824 — ¿el trámite sigue en borrador? Solo ahí una identidad vencida a mano deja de valer.</summary>
    private static bool EsBorrador(ProcedureInstance instance) =>
        string.Equals(instance.Status, Flit.Tramites.Domain.Tramites.Estados.TramiteEstado.Borrador, StringComparison.OrdinalIgnoreCase);

    /// <summary>¿El trámite tiene una validación PROPIA de la parte aprobada+vigente y del documento del actor?</summary>
    private static bool HasLocalVigente(
        ProcedureInstance instance, string parte, string? tipoDoc, string? documento, DateTimeOffset now,
        DateTimeOffset? salidaDeBorradorAt) =>
        instance.BiometricValidations.Any(v =>
            string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigenteParaTramite(v, instance.Status, now, salidaDeBorradorAt)
            && BiometricRules.DocumentoCoincide(v, tipoDoc, documento));

    /// <summary>Tiene el tramite (fuera de borrador) alguna fila propia aprobada y vencida a mano? Solo entonces su salida de borrador cambia el veredicto.</summary>
    internal static bool TieneVencidaManualLocal(ProcedureInstance instance) =>
        !EsBorrador(instance)
        && instance.BiometricValidations.Any(v =>
            v.Status == BiometricEstados.Aprobado && v.ExpiredManuallyAt != null);

    /// <summary>
    /// Salida de borrador del tramite para decidir si conserva una identidad vencida a mano
    /// (<see cref="BiometricRules.ConservaFirmaTrasVencimientoManual"/>). Nula en borrador (no hace falta) y cuando no hay
    /// fila en el historial (legado/migrados: comportamiento previo).
    /// </summary>
    internal static async Task<DateTimeOffset?> SalidaDeBorradorAsync(
        IProcedureInstanceRepository repo, ProcedureInstance instance, CancellationToken ct) =>
        EsBorrador(instance)
            ? null
            : await repo.GetSalidaDeBorradorAtAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false);
}
