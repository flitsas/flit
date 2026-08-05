using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Repositories;

public interface IProcedureInstanceRepository
{
    Task<ProcedureInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lista los trámites de MATRÍCULA INICIAL del tenant (no eliminados) cuyo VIN coincide con
    /// <paramref name="vinNormalizado"/>, EXCLUYENDO <paramref name="excludeInstanceId"/> (el borrador
    /// en curso, para que no se detecte a sí mismo). Proyecta a <see cref="VinTramiteExistente"/> con la
    /// secretaría (nombre del OT) y la fecha del registro previo, ordenado por recencia descendente para
    /// que <see cref="Tramites.Services.VinPolicyEvaluator.EvaluarConflicto"/> tome el primer bloqueante
    /// como referencia del mensaje. El VIN almacenado se compara normalizado (mayúsculas + trim) contra
    /// <paramref name="vinNormalizado"/>, que ya viene de <see cref="Tramites.Services.VinNormalizer"/>.
    /// Solo lectura (HU #10538, R3). Set vacío si no hay coincidencias.
    /// </summary>
    Task<IReadOnlyList<VinTramiteExistente>> FindTramitesByVinAsync(
        Guid tenantId, string vinNormalizado, Guid excludeInstanceId, CancellationToken ct = default);

    /// <summary>
    /// Lista los trámites de TRASPASO del tenant (no eliminados) cuya placa coincide con
    /// <paramref name="placaNormalizada"/>, EXCLUYENDO <paramref name="excludeInstanceId"/> (el
    /// trámite en curso, para que no se detecte a sí mismo). Simétrico a
    /// <see cref="FindTramitesByVinAsync"/> pero para la familia Traspaso (llave placa): el CF-01 de
    /// duplicidad de trámite EN PROCESO (HU #10876) usa este método cuando la familia es Traspaso, tal
    /// como <see cref="FindTramitesByVinAsync"/> se usa cuando la familia es Matrícula Inicial. La
    /// placa almacenada se compara normalizada (mayúsculas + trim) contra
    /// <paramref name="placaNormalizada"/>. Set vacío si no hay coincidencias. Solo lectura.
    /// </summary>
    Task<IReadOnlyList<PlacaTramiteExistente>> FindTramitesByPlacaAsync(
        Guid tenantId, string placaNormalizada, Guid excludeInstanceId, CancellationToken ct = default);

    Task<ProcedureInstance?> GetByIdWithDetailsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con únicamente sus <c>Actors</c>. Query lean para operaciones
    /// PUT/GET de actores que no necesitan FieldValues ni StatusHistory.
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithActorsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    Task<ProcedureInstance?> GetByIdWithAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con el grafo necesario para computar el checklist condicional
    /// (RF30/31/35): <c>Attachments</c> (auto-marcado), <c>Actors</c> (NIT vs persona natural),
    /// <c>FieldValues</c> (servicio especial, tipo de documento del propietario) y
    /// <c>Participants</c> (tramitador). Es el grafo que consume <c>GetChecklistHandler</c>.
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithChecklistGraphAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con sus <c>Actors</c> y <c>Attachments</c>. Query lean para el
    /// cómputo del checklist, que necesita los tipos de documento subidos (auto-marca) y el
    /// tipo de persona de los actores (supresión de <c>cedulas</c> para persona natural, HU #10542).
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithActorsAndAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con TODO el grafo del wizard: actores, field values, adjuntos,
    /// datos comerciales y snapshots de preflight (Slice 4 — wizard server-driven).</summary>
    Task<ProcedureInstance?> GetByIdWithWizardGraphAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus datos comerciales (1:1) para GET/PUT comercial.</summary>
    Task<ProcedureInstance?> GetByIdWithCommercialAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lista las instancias de un tenant (no eliminadas), las más recientes primero, cargando el grafo
    /// del wizard necesario para el resumen de la tabla de operación (Slice M6): field values
    /// (placa/VIN/marca/línea), actores (comprador), adjuntos (FUR), comercial, snapshots de preflight,
    /// biométricas y firmas — el mismo grafo que consume <c>GetWizardStateHandler.ComputeState</c> para
    /// derivar el paso actual. Limitado a <paramref name="limit"/> filas (cap razonable, p.ej. 200).
    /// </summary>
    /// <param name="tenantId">Tenant a listar; <c>null</c> = TODOS los tenants (solo SuperAdmin, #1).</param>
    Task<IReadOnlyList<ProcedureInstance>> ListWithSummaryGraphAsync(Guid? tenantId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Resuelve el nombre (razón social) de cada tenant indicado, para la columna "Compañía" del
    /// listado multi-tenant del SuperAdmin (#1). Devuelve un mapa id→nombre; ids sin tenant se omiten.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTenantNamesAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken ct = default);

    /// <summary>
    /// HU #11056 — resuelve el nombre visible de cada usuario indicado, para la columna "Gestor" del
    /// listado (la persona que radica, <c>created_by_user_id</c>). Devuelve un mapa id→nombre en UNA
    /// consulta (<c>WHERE id IN …</c>): la columna no debe costar una petición por fila. Los ids sin
    /// usuario o con nombre vacío se omiten, y la columna cae al identificador de la fila.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>
    /// Estado de la firma del BAÚL por persona (clave <see cref="Entities.BiometricRules.IdentidadKey"/>),
    /// para las columnas "Firmado" del listado: <c>true</c> = hay firma vigente hoy; <c>false</c> = hay
    /// firma pero ya no sirve (vencida o revocada); AUSENTE = esa persona no tiene ninguna firma.
    /// <para>
    /// Existe porque la columna necesita distinguir «firmada por baúl» de «baúl vencido», y la ruta de
    /// lote de la identidad biométrica (<see cref="ListVigenteApprovedIdentityKeysAsync"/>)
    /// deliberadamente no consulta el baúl para no caer en N+1. Aquí se resuelve en UNA consulta por los
    /// tenants del listado, que es lo que permite usarlo fila a fila.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> ListFirmaBaulVigenciaKeysAsync(
        IReadOnlyCollection<Guid> tenantIds, DateOnly hoy, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus validaciones biométricas (Slice 6).</summary>
    Task<ProcedureInstance?> GetByIdWithBiometricsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lista las validaciones biométricas del tenant a través de TODAS sus instancias no eliminadas,
    /// las más recientes primero, incluyendo la instancia padre (referencia/modalidad) para la vista
    /// transversal del submódulo "Validaciones de Identidad" (HU #10234). Solo lectura (AsNoTracking),
    /// acotada a <paramref name="limit"/> filas (cap de monitoreo, no exporta el histórico completo).
    /// </summary>
    Task<IReadOnlyList<ProcedureInstanceBiometricValidation>> ListBiometricValidationsByTenantAsync(
        Guid tenantId,
        int skip,
        int take,
        BiometricValidationListFilter? filter,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// Cuenta las validaciones biométricas del tenant agrupadas por estado (KPIs del submódulo de
    /// Validaciones). Independiente del cap de filas de <see cref="ListBiometricValidationsByTenantAsync"/>
    /// para que los totales sean exactos. Si <paramref name="filter"/> tiene criterios activos, el conteo
    /// se aplica sobre el mismo conjunto filtrado (HU #10347).
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountBiometricValidationsByEstadoAsync(
        Guid tenantId,
        BiometricValidationListFilter? filter,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con sus validaciones biométricas + actores (Slice M4 — simular biométrica:
    /// resuelve el actor de la parte para poblar nombre/documento/email de la validación aprobada).
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithBiometricsAndActorsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus firmas electrónicas (Slice 7).</summary>
    Task<ProcedureInstance?> GetByIdWithSignaturesAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lista los borradores FINALIZADOS del tenant (status=draft, <c>draft_finalized_at</c> NOT NULL, no
    /// eliminados) donde el sujeto (<paramref name="tipoDoc"/> + <paramref name="documento"/>) es actor de
    /// la <paramref name="parte"/> indicada. Orden determinista: <c>draft_finalized_at</c> ASC, luego
    /// <c>reference_number</c> (HU #10349, AC5). Incluye los actores para resolver la parte; el resto del
    /// grafo lo recargan los handlers de firma/FUR por id. Solo lectura.
    /// </summary>
    Task<IReadOnlyList<ProcedureInstance>> ListDraftFinalizedByActorAsync(
        Guid tenantId, string parte, string tipoDoc, string documento, CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con TODO el grafo necesario para generar el FUR (Slice 7): actores,
    /// field values, adjuntos, comercial, biométricas y firmas.
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithFurGraphAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Busca la validación de identidad APROBADA y VIGENTE más reciente de una persona
    /// (<paramref name="tipoDoc"/> + <paramref name="documento"/>) en CUALQUIER trámite no eliminado del
    /// tenant, para reutilizarla (HU #10350 — reuso de identidad vigente). Vigente = aprobada con
    /// <c>validado_at</c> dentro de los <see cref="Entities.BiometricRules.VigenciaDias"/> días (regla por
    /// fecha de <see cref="Entities.BiometricRules.EsAprobadaVigente"/>, aplicada en memoria sobre los
    /// candidatos). Devuelve null si la persona no tiene ninguna validación vigente. Solo lectura.
    /// </summary>
    Task<ProcedureInstanceBiometricValidation?> FindVigenteApprovedByDocumentAsync(
        Guid tenantId, string tipoDoc, string documento, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// HU #11265 — validaciones EN VUELO (<c>pendiente_envio</c> / <c>enviado</c> / <c>en_proceso</c>)
    /// del documento en el tenant (standalone o ligadas a instancias no eliminadas). Igualdad exacta de
    /// tipo/número como <see cref="FindVigenteApprovedByDocumentAsync"/> (no cambia el gate, AC5).
    /// Solo lectura; lista acotada (máx. 20) ordenada por actividad reciente.
    /// </summary>
    Task<IReadOnlyList<ProcedureInstanceBiometricValidation>> ListInFlightByDocumentAsync(
        Guid tenantId, string tipoDoc, string documento, CancellationToken ct = default);

    /// <summary>
    /// Claves (<see cref="Entities.BiometricRules.IdentidadKey"/>) de todas las identidades APROBADAS y VIGENTES
    /// de los tenants indicados, en UNA consulta. Para el listado de trámites: resuelve la identidad por PERSONA
    /// sin N+1 (HU #10350 — referenciar la identidad vigente, no clonar por trámite). Set vacío si no hay ninguna.
    /// </summary>
    Task<IReadOnlySet<string>> ListVigenteApprovedIdentityKeysAsync(
        IReadOnlyCollection<Guid> tenantIds, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Feature #11066 / HU #11069 — trámites del tenant asociados a la misma identidad
    /// (<paramref name="documents"/> = pares tipo+número): por validación biométrica de la instancia
    /// y/o por actores con ese documento. Mapa por <see cref="Entities.BiometricRules.IdentidadKey"/>.
    /// Solo lectura.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<LinkedProcedureSummary>>> ListLinkedProceduresByIdentityDocumentsAsync(
        Guid tenantId,
        IReadOnlyCollection<(string DocumentType, string DocumentNumber)> documents,
        CancellationToken ct = default);

    /// <summary>
    /// Resuelve una validación biométrica por el hash SHA-256 de su token (acceso PÚBLICO vía
    /// magic-link, sin tenant). Devuelve null si no existe — el caller NO debe filtrar existencia.
    /// </summary>
    Task<ProcedureInstanceBiometricValidation?> GetBiometricByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Resuelve una validación biométrica por su id (acceso PÚBLICO vía webhook de Kyverum, sin tenant).
    /// La correlación del webhook se hace por este id porque viaja incrustado en la <c>webhookUrl</c>
    /// registrada (el cuerpo del webhook no lo repite). Devuelve null si no existe — el caller NO debe
    /// filtrar existencia (HU #10233, AC2/AC3).
    /// </summary>
    Task<ProcedureInstanceBiometricValidation?> GetBiometricByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// HU #10943 (CF-03) — resuelve una validación biométrica por id, aislada por tenant y TRACKEADA
    /// (para editar/reenviar), incluyendo su <see cref="Person"/> (necesaria para
    /// <c>IniciarPrevalidacionHandler.ResolveSubject</c>). Devuelve null si no existe o no pertenece al
    /// tenant. Aplica tanto a validaciones standalone como ligadas a trámite (el handler decide
    /// editabilidad con <c>ProcedureInstanceId</c>/<c>PersonId</c>).
    /// </summary>
    Task<ProcedureInstanceBiometricValidation?> GetBiometricByIdWithPersonAsync(
        Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Cuenta un intento rechazado de Kyverum de forma ATÓMICA e idempotente: en un ÚNICO <c>UPDATE</c>
    /// incrementa <c>attempts</c>, sella <c>last_attempt_at</c> con la clave del intento y REINICIA
    /// <c>reconcile_poll_count</c>, con la guarda <c>status = en_proceso AND last_attempt_at &lt;&gt; @key</c>.
    /// Dos entregas paralelas del MISMO webhook (misma clave) cuentan una sola vez: Postgres bloquea la fila
    /// y la segunda ya no cumple la guarda. Devuelve <c>true</c> si contó (intento nuevo, aún en proceso);
    /// <c>false</c> si fue un redelivery del mismo intento o la validación ya no está en proceso.
    /// </summary>
    Task<bool> TryCountKyverumAttemptAsync(
        Guid validationId, string attemptKey, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Recarga desde la BD los valores de una validación ya rastreada (tras un <c>UPDATE</c> atómico que no
    /// pasó por el change tracker), para que el caller vea el conteo actualizado antes de terminalizar/enriquecer.
    /// </summary>
    Task ReloadBiometricAsync(ProcedureInstanceBiometricValidation validation, CancellationToken ct = default);

    /// <summary>
    /// Bitácora del ciclo de validación de identidad (envío/webhook/descifrado/reconciliación/errores) de una
    /// validación, ordenada por <c>occurred_at</c>. Solo lectura (AsNoTracking).
    /// </summary>
    Task<IReadOnlyList<IdentityValidationAuditEvent>> ListIdentityAuditByValidationAsync(
        Guid validationId, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus participantes del portal (Slice 7 Part B, vista del gestor).</summary>
    Task<ProcedureInstance?> GetByIdWithParticipantsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resuelve un participante del portal por el hash SHA-256 de su token (acceso PÚBLICO vía
    /// magic-link, sin tenant). Carga la instancia con el grafo necesario para agregar el estado de
    /// los pasos del rol (biométricas, firmas, adjuntos). Devuelve null si no existe — el caller NO
    /// debe filtrar existencia (not_found genérico, sin enumeración de PII).
    /// </summary>
    Task<ProcedureInstanceParticipant?> GetParticipantByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Encola un evento de bitácora (append-only) para persistir en el próximo SaveChanges.</summary>
    Task AddEventAsync(ProcedureInstanceEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Documentos que se consultaron REALMENTE en el RUNT dentro de este trámite, en la forma
    /// <c>TIPO|NUMERO</c> de <see cref="Tramites.Services.RuntPersonaConsultada.Key"/>. Lo usa el gate
    /// de actores para exigir la consulta en vez de darla por hecha con el documento digitado.
    /// </summary>
    Task<IReadOnlySet<string>> ListRuntConsultedDocumentKeysAsync(
        Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Último snapshot de preflight de la instancia (por created_at desc), o null.</summary>
    Task<ProcedureInstancePreflightSnapshot?> GetLatestPreflightAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Encola un nuevo snapshot de preflight para persistir en el próximo SaveChanges.</summary>
    Task AddPreflightSnapshotAsync(ProcedureInstancePreflightSnapshot snapshot, CancellationToken ct = default);

    Task<int> CountByTenantAndYearAsync(Guid tenantId, int year, CancellationToken ct = default);

    /// <summary>
    /// Inserta la instancia generando un <c>ReferenceNumber</c> único con formato
    /// <c>TRM-{year}-{seq:D6}</c> a partir de MAX(seq) + 1 por (tenant, year). Si el insert
    /// colisiona contra el constraint <c>uq_procedure_instances_tenant_reference</c> (creaciones
    /// concurrentes), regenera el siguiente seq y reintenta. Si una FK no existe
    /// (tenant/usuario/tipo) devuelve <c>ReferencedEntityMissing</c> (→ 422); si se agotan los
    /// reintentos de referencia devuelve <c>ReferenceConflict</c> (→ 409).
    /// </summary>
    Task<AddProcedureInstanceOutcome> AddWithUniqueReferenceAsync(ProcedureInstance instance, int year, CancellationToken ct = default);

    /// <summary>
    /// Resuelve el <c>FormField.Id</c> de un <paramref name="fieldKey"/> dentro del grafo
    /// steps→sections→fields del <paramref name="procedureTypeId"/>. Null si no existe.
    /// </summary>
    Task<Guid?> GetFormFieldIdByKeyAsync(Guid procedureTypeId, string fieldKey, CancellationToken ct = default);

    Task AddAsync(ProcedureInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Marca explícitamente una entidad NUEVA como <c>Added</c> en el contexto para forzar un
    /// INSERT. Necesario para hijos creados vía colección de navegación cuya PK está mapeada como
    /// store-generated (<c>DEFAULT uuidv7()</c>) pero se asigna en código (<c>Id = Guid.NewGuid()</c>):
    /// EF infiere estado a partir de la PK store-generated y, al verla con valor no-default, asume
    /// que la fila ya existe → la marca <c>Modified</c> → emite UPDATE de 0 filas → DbUpdateConcurrencyException.
    /// <c>Add</c> deja el estado en <c>Added</c> → EF emite INSERT con ese Id.
    /// </summary>
    void Add<TEntity>(TEntity entity) where TEntity : class;

    Task UpdateAsync(ProcedureInstance instance, CancellationToken ct = default);
    void RemoveAttachment(ProcedureInstanceAttachment attachment);

    /// <summary>
    /// <c>true</c> si existe un usuario con ese id en <c>identity.users</c>. Usado como guarda FK
    /// antes de sellar <c>changed_by</c> en <c>status_history</c> (HU #10431): si el sujeto no existe
    /// (proceso automático o <c>sub</c> inválido), el llamador resuelve <c>changed_by</c> a null.
    /// </summary>
    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Olvida TODO lo rastreado por el contexto (HU #11029). Necesario cuando un flujo encadena varios
    /// handlers que guardan sobre la misma instancia: los adjuntos borrados por el handler anterior
    /// quedan como referencias muertas en las colecciones de navegación ya cargadas, y volver a
    /// borrarlos o leerlos revienta con un error de concurrencia («se esperaba afectar 1 fila, se
    /// afectaron 0») o intenta abrir del almacenamiento un archivo que ya no existe.
    /// </summary>
    void ResetTracking();

    /// <summary>
    /// Persiste la unidad de trabajo protegiendo la concurrencia optimista de
    /// <c>procedure_instances.row_version</c> (RNF01, N 03): si otro proceso transicionó la
    /// instancia entre la carga y el commit, devuelve <c>false</c> SIN efectos parciales
    /// (la capa Application no referencia EF, por eso el mapeo de
    /// <c>DbUpdateConcurrencyException</c> vive en el repositorio). <c>true</c> = commit OK.
    /// </summary>
    Task<bool> SaveChangesWithConcurrencyGuardAsync(CancellationToken ct = default);

    /// <summary>
    /// Página del historial de transiciones de estado de la instancia (HU-2 N03, RF05), ordenada
    /// por <c>changed_at</c> DESC, con el nombre del usuario resuelto contra <c>identity.users</c>
    /// (null si el usuario no existe). Devuelve <c>null</c> si la instancia no existe o no
    /// pertenece al tenant (→ 404); si existe pero no tiene historial, página vacía con Total 0.
    /// </summary>
    Task<(IReadOnlyList<ProcedureInstanceStatusHistoryEntry> Items, int Total)?> GetStatusHistoryPageAsync(
        Guid id, Guid tenantId, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Resuelve el <c>DisplayName</c> de un usuario contra <c>identity.users</c> (operador que radica
    /// una generación de impronta desde el trámite). Null si el usuario no existe.
    /// </summary>
    Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// HU #10872 (AC1) — field values ACTUALES de la instancia, proyección lean (sin el resto del
    /// grafo del wizard). Lo usan los callers que transicionan a <c>subsanacion</c> pero no cargan
    /// <c>GetByIdWithWizardGraphAsync</c> (p. ej. <c>ConsultarEstadoQuipuxHandler</c>) para capturar el
    /// snapshot baseline (<see cref="Tramites.Services.FieldValueSnapshot.Capture"/>) del diff que la
    /// re-radicación computará. Lista vacía si la instancia no existe o no tiene field values.
    /// </summary>
    Task<IReadOnlyList<ProcedureInstanceFieldValue>> GetFieldValuesAsync(
        Guid instanceId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// HU #10872 (AC1) — <c>metadata</c> (jsonb, deserializable con
    /// <c>SubsanacionObservation.FromJson</c>) del registro MÁS RECIENTE de historial con
    /// snapshot de field values (activación de subsanación, observación OT, o legado
    /// <c>to_status = 'subsanacion'</c>): baseline del DIFF al re-radicar
    /// (<c>rechazado → entregado</c>). <c>null</c> si no hay baseline (fail-safe del caller:
    /// re-evalúa todos los gates).
    /// </summary>
    Task<string?> GetLatestSubsanacionMetadataAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// HU #10955 (AC2/AC3/AC5) — actor MÁS RECIENTE (por <c>CreatedAt</c>) de esa persona
    /// (<paramref name="documentType"/> + <paramref name="documentNumber"/>) en CUALQUIER trámite NO
    /// eliminado del tenant, para precargar sus datos de CONTACTO (ciudad, email, dirección, teléfono)
    /// en el paso de actores. Aislamiento por tenant explícito en el <c>WHERE</c> (AC5), como el resto
    /// del repositorio. Devuelve <c>null</c> si la persona nunca ha sido actor de un trámite del
    /// tenant — el caller responde 200 con los campos vacíos, NUNCA 404 (AC3). Solo lectura
    /// (AsNoTracking).
    /// </summary>
    Task<ProcedureInstanceActor?> FindLatestActorContactAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Listado FILTRADO y ORDENADO server-side (a diferencia de <see cref="ListWithSummaryGraphAsync"/>,
    /// que trae el TOP-N más reciente sin filtros ni paginación real): el <c>WHERE</c>/<c>ORDER BY</c> se
    /// resuelve en SQL sobre columnas propias o denormalizadas (VIN/placa/vendedor/comprador — migración
    /// TramitesCamposBusqueda), NUNCA en memoria. Carga el mismo grafo que
    /// <see cref="ListWithSummaryGraphAsync"/> (necesario para <c>ListProcedureInstancesHandler.ToSummary</c>)
    /// solo para las filas de la página pedida. <paramref name="tenantId"/> <c>null</c> = TODOS los
    /// tenants (superadmin, #1). Devuelve también el TOTAL de filas que matchean el filtro (sin
    /// paginar), para que el caller arme la paginación.
    /// </summary>
    Task<(IReadOnlyList<ProcedureInstance> Items, int Total)> ListWithSummaryGraphFilteredAsync(
        Guid? tenantId,
        int skip,
        int take,
        ProcedureInstanceListFilter filter,
        ProcedureInstanceSortBy sortBy,
        SortDirection direction,
        CancellationToken ct = default);
}

/// <summary>
/// Proyección de lectura de una fila de <c>procedure_instance_status_history</c> con el nombre
/// del usuario que ejecutó la transición ya resuelto (HU-2 N03).
/// </summary>
public sealed record ProcedureInstanceStatusHistoryEntry(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    Guid? ChangedByUserId,
    string? ChangedByName,
    string? Reason)
{
    /// <summary>
    /// Metadata jsonb crudo del evento. En eventos de migración V1→V2 conserva el actor REAL de V1
    /// (<c>usuario</c>/<c>usuario_rol</c>/<c>usuario_email</c>) y el marcador <c>origen=migration_v1</c>,
    /// que la capa de aplicación usa para mostrar el usuario real en vez del sistema "Migración V1".
    /// </summary>
    public string? Metadata { get; init; }
}
