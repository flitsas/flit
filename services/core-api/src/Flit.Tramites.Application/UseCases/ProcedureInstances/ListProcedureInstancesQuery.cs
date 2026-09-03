using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Resumen por instancia para la tabla de operación (Slice M6). Los nombres de campo son contrato
/// con el frontend: NO renombrar sin coordinar. Campos derivados de field_values, actores y el grafo
/// del wizard; <c>PasoActual</c>/<c>TotalPasos</c> reusan los gates del wizard server-driven.
/// </summary>
public sealed record InstanceSummaryDto(
    Guid Id,
    string ReferenceNumber,
    string Modalidad,            // "matricula_inicial" | "traspaso"
    string Estado,               // TramiteEstado (N 03): "borrador" | "preparado" | "entregado" | ...
    string? Placa,
    string? Vin,
    string? VehiculoMarca,
    string? VehiculoLinea,
    string? CompradorNombre,
    string? CompradorDocumento,
    string? OrganismoTransito,   // nombre del OT elegido (field_value transit_office_name)
    int PasoActual,              // 1..TotalPasos
    int TotalPasos,              // 5 matrícula | 6 traspaso
    DateTimeOffset CreatedAt,
    Guid TenantId,                            // compañía dueña (para el scoping/columna del superadmin, #1)
    string? CompaniaNombre,                   // razón social; solo se resuelve en el listado multi-tenant (superadmin)
                                              // HU #10350 — desacople de la validación de identidad async. Estos campos alimentan los chips
                                              // del listado ("Pendiente validación" / "Pendiente firma" / "Radicar") y la acción de la fila sin
                                              // que el frontend recalcule gates. Opcionales (default) para compat con consumidores existentes.
    DateTimeOffset? DraftFinalizedAt = null,
    string? IdentityValidationStatus = null,  // aprobado | en_proceso | rechazado | null (sin iniciar)
    bool SignaturePending = false,            // traspaso: compraventa de alguna parte sin firmar
    bool CanSubmit = false,                   // gates de radicación satisfechos (mismo cómputo que el wizard)
    bool Prioritario = false,                 // HU #10536 — marcado prioritario (ordenamiento con primacía)
                                              // HU #11020 — la parte SALIENTE del traspaso, para identificar el
                                              // trámite en el dashboard sin abrirlo. null en matrícula inicial.
    string? VendedorNombre = null,
    string? VendedorDocumento = null,
    bool SubsanacionActiva = false,           // flag de edición sobre rechazado
    int SubsanacionCount = 0,                 // veces que se activó la subsanación
    string? UltimoRechazoMotivo = null,      // reason (texto libre) del último rechazo OT
                                              // ICT (servicio v1 pauseDraftProcess / bandera starts_procedure_in_paused):
                                              // el trámite está pausado y no avanza; la observación es informativa (dashboard).
    bool IsPaused = false,
    string? PausedObservation = null,
    // ICT — origen del trámite ('ict' para los creados por la integración). Habilita en la UI la acción
    // de pausar/reanudar SOLO para esos; null/"" en trámites de plataforma (no se ofrece la acción).
    // OJO: es el valor CRUDO. La columna "Fuente" del listado NO lo usa directo, sino `Fuente` (abajo),
    // que además contempla los trámites migrados de V1.
    string? Origin = null,
    // Sub-estado de placa (null | preasignado | asignado | terminado), ortogonal a Estado (HU11037).
    string? PlateFlowStatus = null,
                                              // HU #11056 — columnas de seguimiento del listado. Todo se DERIVA del grafo que ya
                                              // carga ListWithSummaryGraphAsync; lo único que cuesta una consulta extra es el
                                              // nombre del gestor (resuelto en lote, nunca por fila).
    DateTimeOffset? UpdatedAt = null,         // última modificación; null si nunca se modificó tras crearse
    string? GestorNombre = null,              // persona que radica (created_by_user_id → DisplayName)
    string Fuente = TramiteFuente.Dashboard,  // dashboard | integracion | migrado (ver TramiteFuente)
                                              // Cómo queda ACREDITADA cada parte (ajuste del PO): pendiente | firmado |
                                              // rechazado, por validación de identidad o firma del baúl — NO por la firma
                                              // electrónica de la compraventa. null = no aplica (vendedor en matrícula).
    string? FirmaVendedorEstado = null,
    string? FirmaCompradorEstado = null,
                                              // Expediente consolidado del wizard (adjunto tipo 'consolidado') ya generado. El
                                              // id viaja para que la fila lo previsualice sin consultar los adjuntos (HU #11055).
    Guid? ConsolidadoAttachmentId = null,
                                              // ADR-0050 — identidad del TIPO, no solo su familia. En MATRICULAS y TRASPASO la
                                              // familia alcanza para identificar la fila, pero OTROS agrupa quince tipos
                                              // distintos: un blindaje, un cambio de color y un levantamiento de prenda se
                                              // veían los tres como «Otros» y no había forma de distinguirlos sin abrirlos.
                                              // La navegación del tipo ya viene cargada en el grafo del listado.
    string? TipoNombre = null,
    string? TipoCodigo = null,
                                              // Rótulo del paso en curso, tomado del recorrido del TIPO. El frontend lo derivaba de
                                              // un array de nombres por familia, que para OTROS estaba vacío —salía «—»— y que de
                                              // todos modos no puede acertar: cada tipo tiene su propio recorrido desde ADR-0050.
    string? PasoNombre = null);

/// <summary>
/// Lista las instancias de un tenant (más recientes primero, cap del repo) y las mapea a
/// <see cref="InstanceSummaryDto"/> para la tabla de operación.
/// </summary>
public sealed class ListProcedureInstancesHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Tope de filas devueltas (la tabla muestra un resumen, no es un export paginado).</summary>
    public const int MaxItems = 200;

    private const string BuyerActorType = "comprador";
    private const string SellerActorType = "vendedor";

    /// <summary>
    /// Lista las instancias visibles para el caller. <paramref name="tenantId"/> <c>null</c> = TODAS
    /// las compañías (#1, el superadmin ve todo); un usuario de compañía siempre llega con su
    /// <paramref name="tenantId"/>, resuelto del JWT por el middleware.
    /// <para>
    /// HU #11056 — el nombre de compañía ya no depende de si el caller es superadmin: la columna
    /// "Gestor" lo necesita en los dos listados. El aislamiento por compañía lo sigue garantizando
    /// <paramref name="tenantId"/>, que es lo único que llega al <c>WHERE</c>.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<InstanceSummaryDto>> HandleAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        var instances = await repo.ListWithSummaryGraphAsync(tenantId, MaxItems, ct);

        // HU #11056 — la razón social se resuelve SIEMPRE, no solo para el superadmin: la columna
        // "Gestor" es «empresa – persona» también para un usuario de compañía. Para un solo tenant la
        // consulta trae una fila (los ids van distintos), así que el coste es el mismo de antes.
        IReadOnlyDictionary<Guid, string> nombres =
            await repo.GetTenantNamesAsync(instances.Select(i => i.TenantId).ToList(), ct) ?? EmptyNames;

        // Nombre de la persona que radica, en lote por los created_by_user_id del listado (sin N+1).
        IReadOnlyDictionary<Guid, string> gestores =
            await repo.GetUserDisplayNamesAsync(instances.Select(i => i.CreatedByUserId).ToList(), ct)
            ?? EmptyNames;

        // Identidad PER-PERSONA (documento) para los chips/progreso: se referencia la identidad vigente de
        // la persona en N trámites sin clonar (HU #10350). Una sola consulta para TODOS los tenants del
        // listado (WHERE tenant_id IN …) → sin N+1.
        var now = DateTimeOffset.UtcNow;
        IReadOnlySet<string> identidadKeys = await repo.ListVigenteApprovedIdentityKeysAsync(
            instances.Select(i => i.TenantId).Distinct().ToList(), now, ct) ?? new HashSet<string>();

        // Ajuste del PO sobre HU #11056 — las columnas "Firmado" acreditan por identidad O por firma del
        // baúl, y tienen que distinguir «baúl vigente» de «baúl vencido». UNA consulta para todos los
        // tenants del listado, con la MISMA llave que la identidad (tenant|TIPO|NÚMERO).
        // HU #11667 — ese mismo diccionario alimenta ahora la acreditación por baúl de los chips: sin él,
        // el chip contradecía al gate de radicación y al FUR. Pasarlo no cuesta ninguna consulta.
        var hoy = DateOnly.FromDateTime(now.ToOffset(ColombiaUtcOffset).DateTime);
        IReadOnlyDictionary<string, bool> firmaBaul = await repo.ListFirmaBaulVigenciaKeysAsync(
            instances.Select(i => i.TenantId).Distinct().ToList(), hoy, ct) ?? EmptyFirmaBaul;

        return instances
            .Select(e => ToSummary(
                e,
                IdentityApprovalResolver.ApprovedPartiesFromKeys(e, identidadKeys, now, firmaBaul),
                nombres.GetValueOrDefault(e.TenantId),
                gestores.GetValueOrDefault(e.CreatedByUserId),
                firmaBaul))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<Guid, string> EmptyNames = new Dictionary<Guid, string>();

    private static readonly IReadOnlyDictionary<string, bool> EmptyFirmaBaul = new Dictionary<string, bool>();

    /// <summary>Hora de Colombia (UTC-5, sin DST): la vigencia del baúl se cuenta por día calendario local.</summary>
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    internal static InstanceSummaryDto ToSummary(
        ProcedureInstance e,
        IReadOnlySet<string> identidadAprobadaPartes,
        string? companiaNombre = null,
        string? gestorNombre = null,
        IReadOnlyDictionary<string, bool>? firmaBaulPorPersona = null)
    {
        var fv = e.FieldValues.ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);
        var buyer = e.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, BuyerActorType, StringComparison.OrdinalIgnoreCase));
        // HU #11020 — el vendedor solo existe en traspaso; en matrícula queda null y la columna sale vacía.
        var seller = e.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, SellerActorType, StringComparison.OrdinalIgnoreCase));

        var modalidad = e.Family;
        var modalidadCode = ProcedureFamilyCodes.ToCode(modalidad);

        // ADR-0051 — quién valida identidad y quién firma lo declara el TIPO (`biometricActors` /
        // `signatureActors`), no la familia. Con la familia, `TRASPASO_UNILATERAL` heredaba las dos
        // partes de un traspaso estándar y el listado pedía identidad y firma de un comprador que
        // en ese trámite no comparece. Se resuelve una sola vez por fila: el perfil ya viene con el
        // grafo del listado (`ListWithSummaryGraphAsync` incluye `ProcedureType`), sin consulta extra.
        var profile = ProcedureTypeGateProfile.FromJson(e.ProcedureType?.GateProfile);
        var partesIdentidad = PartesDeclaradas.Identidad(profile);

        // Estado server-driven del wizard: misma fuente de verdad que el frontend (canSubmit) y de
        // la que se deriva el progreso (PasoActual/TotalPasos). Se computa una sola vez.
        var state = GetWizardStateHandler.ComputeState(e, identidadAprobadaPartes);
        var (pasoActual, totalPasos) = ComputeProgress(state, e.Status);

        return new InstanceSummaryDto(
            e.Id,
            e.ReferenceNumber,
            modalidadCode,
            e.Status,
            Field(fv, "plate"),
            Field(fv, "vin"),
            Field(fv, "vehicle_brand"),
            Field(fv, "vehicle_line"),
            string.IsNullOrWhiteSpace(buyer?.FullName) ? null : buyer.FullName,
            string.IsNullOrWhiteSpace(buyer?.DocumentNumber) ? null : buyer.DocumentNumber,
            Field(fv, "transit_office_name"),
            pasoActual,
            totalPasos,
            e.CreatedAt,
            e.TenantId,
            string.IsNullOrWhiteSpace(companiaNombre) ? null : companiaNombre,
            e.DraftFinalizedAt,
            DeriveIdentityStatus(e, partesIdentidad, identidadAprobadaPartes),
            DeriveSignaturePending(e, profile),
            state.CanSubmit,
            e.Prioritario,
            string.IsNullOrWhiteSpace(seller?.FullName) ? null : seller.FullName,
            string.IsNullOrWhiteSpace(seller?.DocumentNumber) ? null : seller.DocumentNumber,
            e.SubsanacionActiva,
            e.SubsanacionCount,
            DeriveUltimoRechazoMotivo(e),
            // El ORDEN de estos argumentos debe seguir al del record posicional de arriba: son casi
            // todos string?/bool, así que una desalineación compilaría igual e intercambiaría valores.
            e.IsPaused,
            // Solo tiene sentido mostrar la nota cuando está pausado; se limpia al reanudar de todos modos.
            e.IsPaused ? e.PausedObservation : null,
            e.Origin,
            e.PlateFlowStatus,
            e.UpdatedAt,
            string.IsNullOrWhiteSpace(gestorNombre) ? null : gestorNombre.Trim(),
            TramiteFuente.Desde(e.Origin, e.IsMigrated),
            DeriveFirmaParte(e, partesIdentidad, SellerActorType, identidadAprobadaPartes, firmaBaulPorPersona ?? EmptyFirmaBaul),
            DeriveFirmaParte(e, partesIdentidad, BuyerActorType, identidadAprobadaPartes, firmaBaulPorPersona ?? EmptyFirmaBaul),
            DeriveConsolidadoAttachmentId(e),
            e.TypeName,
            e.TypeCode,
            // `pasoActual` es 1-based sobre el MISMO `state.Steps` del que sale, así que el rótulo
            // es el de esa posición. Null si el tipo no tiene recorrido parametrizado.
            pasoActual >= 1 && pasoActual <= state.Steps.Count
                ? state.Steps[pasoActual - 1].Label
                : null);
    }

    /// <summary>
    /// Estado de "Firmado" de UNA parte (ajuste del PO sobre la HU #11056). La columna acredita a la
    /// parte por <b>validación de identidad o firma del baúl</b>, no por la firma electrónica de la
    /// compraventa. Precedencia, en este orden:
    /// <list type="number">
    ///   <item><b>firmado</b> — identidad aprobada y vigente, o firma del baúl vigente. Lo positivo
    ///   gana: una identidad aprobada acredita a la parte aunque su baúl esté caducado.</item>
    ///   <item><b>rechazado</b> — identidad rechazada/expirada, o firma del baúl vencida.</item>
    ///   <item><b>pendiente</b> — no hay nada hecho todavía.</item>
    /// </list>
    /// <c>null</c> = NO APLICA: el tipo no manda a esta parte a validar identidad — el vendedor en
    /// matrícula inicial y, desde ADR-0051, el comprador en <c>TRASPASO_UNILATERAL</c>, donde el
    /// locatario formaliza a su nombre y el único que acredita es el propietario.
    ///
    /// <para><b>Bug #11670 — el baúl solo cuenta si <see cref="FirmaBaulCobertura.Aplica"/> lo permite.</b>
    /// Esta columna resolvía el baúl por su cuenta: bastaba con encontrar la llave de la persona en el
    /// diccionario, sin mirar si el actor era jurídico ni qué mecanismo de firma eligió el gestor. Con la
    /// HU #11667 el chip de identidad de la MISMA fila pasó a respetar el mecanismo, y las dos superficies
    /// contiguas se contradecían: chip «pendiente» y columna «firmado» para un actor jurídico con
    /// <c>mecanismoFirma = identidad</c> y baúl vigente. Es la raíz del Bug #11141 — consumidores que
    /// resuelven el baúl por su cuenta en vez de delegar en el predicado único—, así que aquí también se
    /// delega. Cuando el baúl no procede, no cuenta <i>en ninguno de los dos sentidos</i>: ni acredita ni
    /// rechaza, y la parte queda a merced de su validación de identidad. Coste: cero consultas nuevas.</para>
    ///
    /// <para>La asimetría documentada en la HU #11667 se mantiene: el diccionario se materializa sin mirar
    /// el flag <c>signature_vault_enabled</c> del tenant. Filtrarlo aquí exigiría una consulta de
    /// configuración por tenant en la ruta de lote; la corrección pertenece al origen de las claves, que
    /// sirve a la columna y al chip a la vez.</para>
    ///
    /// <para><b>Reporte 2026-09-03 — el rechazo NO puede sobrevivir a un cambio de persona.</b> El chequeo
    /// de rechazo miraba <c>PartyRole</c> con <c>Any(...)</c> sobre TODO el historial: al reemplazar al
    /// representante legal de la parte (correo distinto → HU #10880 expira la validación previa y reenvía
    /// una nueva), la fila vieja del representante ANTERIOR seguía teniendo <c>PartyRole = parte</c> y
    /// <c>Expirado</c>, así que seguía marcando «Rechazado» al representante NUEVO aunque a este nunca se
    /// le hubiera enviado nada — mientras el wizard, que sí correlaciona por actor/documento, mostraba
    /// «Pendiente de validación». Ahora se mira solo la fila MÁS RECIENTE que corresponda al documento del
    /// sujeto ACTUAL (<see cref="BiometricRules.DocumentoCoincide"/>, la misma defensa que ya usaba el gate
    /// de identidad para este mismo problema), no cualquier fila histórica del rol.</para>
    /// </summary>
    private static string? DeriveFirmaParte(
        ProcedureInstance e,
        IReadOnlyList<string> partesIdentidad,
        string parte,
        IReadOnlySet<string> identidadAprobadaPartes,
        IReadOnlyDictionary<string, bool> firmaBaulPorPersona)
    {
        // ADR-0051 — la columna solo acredita a las partes que el tipo manda a validar. Antes se
        // preguntaba por la familia y el corte solo contemplaba al vendedor: el comprador se daba por
        // presente SIEMPRE, así que un TRASPASO_UNILATERAL mostraba «pendiente» eterno en una parte
        // que ese trámite nunca convoca.
        if (!PartesDeclaradas.Incluye(partesIdentidad, parte))
            return null;

        var actor = e.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));

        // Sujeto de identidad ACTUAL de esta parte (el representante legal cuando el actor es jurídico).
        // Se resuelve una sola vez y se reusa para el baúl y para filtrar las validaciones propias: ambos
        // deben acreditar/rechazar a la persona que hoy ocupa el rol, no a quien lo ocupó antes.
        var subject = actor is not null ? IdentitySubjectResolver.For(actor) : null;

        // Firma del baúl de la PERSONA que acredita a esta parte, resuelta con la misma llave que la
        // identidad. Bug #11670: solo se mira si el baúl PROCEDE para este actor —actor jurídico y
        // mecanismo de firma compatible—, el mismo predicado que usan el gate de radicación, el chip del
        // listado y el FUR.
        bool? baulVigente = null;
        if (FirmaBaulCobertura.Aplica(actor)
            && !string.IsNullOrWhiteSpace(subject?.TipoDocumento)
            && !string.IsNullOrWhiteSpace(subject?.NumeroDocumento))
        {
            var key = BiometricRules.IdentidadKey(
                e.TenantId, subject!.TipoDocumento, subject.NumeroDocumento);
            if (firmaBaulPorPersona.TryGetValue(key, out var vigente))
                baulVigente = vigente;
        }

        if (identidadAprobadaPartes.Contains(parte) || baulVigente == true)
            return FirmaParteEstados.Firmado;

        // Rechazo explícito de la identidad de ESTA parte: la fila MÁS RECIENTE cuyo rol y documento
        // correspondan al sujeto actual (no cualquier fila histórica de quien ocupó el rol antes).
        var ultimaPropia = e.BiometricValidations
            .Where(v => string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
                && BiometricRules.DocumentoCoincide(v, subject?.TipoDocumento, subject?.NumeroDocumento))
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault();
        var rechazada = ultimaPropia?.Status is BiometricEstados.Rechazado or BiometricEstados.Expirado;

        return rechazada || baulVigente == false
            ? FirmaParteEstados.Rechazado
            : FirmaParteEstados.Pendiente;
    }

    /// <summary>
    /// HU #11055 — adjunto del expediente consolidado del wizard (tipo <c>consolidado</c>) ya generado,
    /// o <c>null</c> si no existe. El id viaja en el resumen para que la fila lo previsualice con el
    /// endpoint de URL prefirmada sin consultar los adjuntos del trámite (una petición por fila).
    /// <para>
    /// Es el consolidado del GESTOR, no el <c>consolidado_maestro</c> del organismo de tránsito: son
    /// dos documentos distintos y el listado de operación muestra el primero.
    /// </para>
    /// </summary>
    private static Guid? DeriveConsolidadoAttachmentId(ProcedureInstance e) =>
        e.Attachments
            .Where(a => string.Equals(a.Tipo, ConsolidadoAttachmentTipo, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault()?.Id;

    private const string ConsolidadoAttachmentTipo = "consolidado";

    /// <summary>
    /// Motivo (texto libre) de la última transición REAL a <c>rechazado</c> (p. ej. entregado→rechazado
    /// del OT/Quipux). Se excluyen entradas auditoría <c>rechazado→rechazado</c> (activar subsanación),
    /// para no mostrar textos del operador como "Subsanación iniciada…".
    /// </summary>
    private static string? DeriveUltimoRechazoMotivo(ProcedureInstance e)
    {
        var latest = e.StatusHistory
            .Where(h => string.Equals(h.ToStatus, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(h.FromStatus, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(h.Reason))
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .FirstOrDefault();
        return latest?.Reason?.Trim();
    }

    /// <summary>
    /// Estado agregado de la validación de identidad para los chips del listado (HU #10350): <c>aprobado</c>
    /// si TODAS las partes requeridas tienen una validación aprobada; <c>en_proceso</c> si alguna relevante
    /// está enviada/en proceso; <c>rechazado</c> si alguna relevante quedó rechazada/expirada; <c>null</c> si
    /// no hay ninguna validación iniciada. En matrícula la única parte (comprador) puede venir con
    /// <c>Parte</c> null o "comprador".
    ///
    /// <para>ADR-0051 — las partes requeridas son las que declara <c>biometricActors</c>, no las de la
    /// familia: en <c>TRASPASO_UNILATERAL</c> es solo el vendedor, y exigirle también al comprador
    /// dejaba el chip clavado en «sin iniciar» aunque el propietario ya hubiera validado.</para>
    /// </summary>
    private static string? DeriveIdentityStatus(
        ProcedureInstance e, string[] partes, IReadOnlySet<string> identidadAprobadaPartes)
    {

        // Aprobado PER-PERSONA: TODAS las partes requeridas tienen identidad vigente aprobada (referenciada,
        // aunque el trámite no tenga fila propia). Se evalúa ANTES de mirar filas locales, porque un trámite
        // que referencia una identidad de otro trámite no tiene validaciones propias.
        if (partes.All(identidadAprobadaPartes.Contains))
            return BiometricEstados.Aprobado;

        // Estados NO terminales/aprobados sí dependen de las validaciones PROPIAS del trámite (una captura en
        // curso o rechazada vive en su instancia): en_proceso / rechazado / sin iniciar.
        //
        // Filas SIN rol (legacy de matrícula) solo se atribuyen cuando la ÚNICA parte requerida es el
        // comprador — mismo criterio que ya aplica `BiometricaCommand` al listar. Con cualquier otra
        // combinación (traspaso, y también el TRASPASO_UNILATERAL de una sola parte vendedora) se
        // exige coincidencia exacta de rol, para no atribuirle a una parte la captura de otra.
        var legacySinRol = partes.Length == 1
            && string.Equals(partes[0], BuyerActorType, StringComparison.OrdinalIgnoreCase);
        bool Relevant(ProcedureInstanceBiometricValidation v) =>
            partes.Any(p => string.Equals(v.PartyRole, p, StringComparison.OrdinalIgnoreCase))
            || (legacySinRol && v.PartyRole is null);

        var relevant = e.BiometricValidations.Where(Relevant).ToList();
        if (relevant.Count == 0)
            return null;

        if (relevant.Any(v => v.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso))
            return BiometricEstados.EnProceso;

        if (relevant.Any(v => v.Status is BiometricEstados.Rechazado or BiometricEstados.Expirado))
            return BiometricEstados.Rechazado;

        return null;
    }

    /// <summary>
    /// Firma de la compraventa pendiente: alguna de las partes firmantes aún no tiene su firma
    /// <c>firmada</c>. Donde no hay compraventa que firmar → siempre false.
    ///
    /// <para>ADR-0051 — las dos preguntas las responde el TIPO, no la familia: si el expediente
    /// autogenera compraventa (<c>generatesSaleDocument</c>, ADR-0035) y quiénes la firman
    /// (<c>signatureActors</c>). <c>TRASPASO_UNILATERAL</c> es de familia traspaso y NO genera
    /// compraventa —el locatario ya tenía el vehículo por el contrato de leasing—, así que con el
    /// criterio anterior el listado le reclamaba para siempre la firma de un documento inexistente.</para>
    /// </summary>
    private static bool DeriveSignaturePending(ProcedureInstance e, ProcedureTypeGateProfile profile)
    {
        if (!profile.GeneratesSaleDocumentAllowed(e.ProcedureType?.Family))
            return false;

        bool Firmada(string parte) => e.Signatures.Any(s =>
            string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase)
            && s.Estado == SignatureEstados.Firmada);

        return !PartesDeclaradas.Firma(profile).All(Firmada);
    }

    /// <summary>
    /// Computa (PasoActual, TotalPasos) reusando el estado server-driven del wizard
    /// (<see cref="GetWizardStateHandler.ComputeState"/>) — misma fuente de verdad que los gates
    /// (VIN consultado, documentos obligatorios, comprador presente, biométrica aprobada, FUR generado).
    /// <para><c>PasoActual</c> = paso ACTIVO (1-based) = la "frontera" del flujo: el primer paso aún
    /// no <c>complete</c>. Es donde el wizard reanuda (Track B), de modo que la columna "Paso" del
    /// listado coincide con el paso en que se abre el trámite. Si todos los pasos están completos, o
    /// la instancia ya está radicada (Submitted o posterior), se reporta <c>PasoActual = TotalPasos</c>.</para>
    /// </summary>
    private static (int PasoActual, int TotalPasos) ComputeProgress(WizardStateDto state, string status)
    {
        var total = state.TotalSteps;

        if (!string.Equals(status, TramiteEstado.Borrador, StringComparison.OrdinalIgnoreCase))
            return (total, total);

        // Frontera = primer paso no completo (mismo criterio que frontierIndex del frontend).
        // PasoActual 1-based = frontera + 1; si no hay incompletos, el último paso.
        var frontier = state.Steps
            .ToList()
            .FindIndex(s => !string.Equals(s.Status, "complete", StringComparison.Ordinal));
        var paso = frontier < 0 ? total : Math.Min(frontier + 1, total);
        return (paso, total);
    }

    private static string? Field(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}
