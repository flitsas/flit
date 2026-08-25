namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstance
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureTypeId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = Tramites.Estados.TramiteEstado.Borrador;

    // Rework trámites (Slice 1) — checklist explícito.
    // ADR-0050: modalidad_entrada y tipologia_codigo se eliminaron. La clasificación del expediente
    // se deriva del tipo (Family / TypeCode / TypeName, más abajo).
    public string ChecklistEstado { get; set; } = "{}";

    /// <summary>
    /// Familia del expediente (ADR-0050), derivada del tipo. Sustituye a <see cref="ModalidadEntrada"/>,
    /// que solo tenía dos valores y colapsaba OTROS en matrícula.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si la navegación <see cref="ProcedureType"/> no está cargada. Se prefiere fallar ruidosamente a
    /// devolver un default: un expediente clasificado por accidente como OTROS elegiría mal el flujo,
    /// los documentos y las causales de rechazo.
    /// </exception>
    public Enums.ProcedureFamily Family =>
        Enums.ProcedureFamilyCodes.FromCodeOrOtros(RequireProcedureType().Family);

    /// <summary>
    /// Código canónico del tipo (<c>MATRICULA_NUEVA</c>, <c>BLINDAJE</c>, …). Es también la tipología:
    /// ADR-0050 elimina el catálogo de tipologías paralelo.
    /// </summary>
    public string TypeCode => RequireProcedureType().Code;

    /// <summary>Código persistido de la familia, para DTOs, filtros y exportes.</summary>
    public string FamilyCode => Enums.ProcedureFamilyCodes.ToCode(Family);

    /// <summary>Etiqueta de negocio del tipo: la que deben rotular FUR, portada y mandato.</summary>
    public string TypeName => RequireProcedureType().Name;

    private ProcedureType RequireProcedureType() =>
        ProcedureType ?? throw new InvalidOperationException(
            $"La navegación ProcedureType no está cargada en la instancia {Id}. "
            + "Usa un método del repositorio que la incluya (todos los GetBy* lo hacen desde ADR-0050) "
            + "o carga el tipo antes de leer su clasificación.");

    public Guid? TransitOfficeId { get; set; }

    /// <summary>
    /// HU #10879 — paso actual PERSISTIDO del wizard (autosave del avance por pasos). Guarda la
    /// <c>Key</c> del paso del contrato del wizard (matrícula: <c>consulta_vin|documentos|comprador|
    /// identidad|fur</c>; traspaso: <c>consulta|documentos|vendedor|comprador|comercial|fur</c>). Solo
    /// se escribe en <c>borrador</c> y una vez que la consulta del vehículo está completa (AC1). Al
    /// reabrir el borrador PRIMA como punto de retoma (AC2); mientras sea <c>null</c> el frontend cae al
    /// paso DERIVADO de los gates (comportamiento previo, sin regresión). Columna agregada por migración
    /// SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea al modelo EF.
    /// </summary>
    public string? CurrentStep { get; set; }

    /// <summary>
    /// Feature #10587 / HU #10785 — sub-estado INTERNO del flujo de asignación de placa, ortogonal al
    /// <see cref="Status"/> global (que permanece en <c>entregado</c> durante todo el sub-flujo). Valores:
    /// <c>null</c> (trámite sin ruta de placa, comportamiento estándar), <c>preasignado</c> (entregado al
    /// OT, esperando placa) y <c>asignado</c> (placa registrada; pendiente de SOAT + recepción del OT).
    /// Ver <see cref="Tramites.Estados.PlateFlowStatus"/>. Columna agregada por migración SQL cruda
    /// (la tabla está ExcludeFromMigrations); aquí solo se mapea al modelo EF.
    /// </summary>
    public string? PlateFlowStatus { get; set; }

    /// <summary>
    /// Marca de "borrador finalizado" (HU #10349, fase 2). El gestor finaliza la captura de datos
    /// (actores, documentos, organismo) y el trámite queda en <c>draft</c> a la espera de la validación
    /// de identidad async del cliente. Cuando llega <c>IdentityValidationCompleted</c> (aprobado), el
    /// consumidor de outbox firma/encadena automáticamente los borradores finalizados del sujeto. Null
    /// mientras el borrador no se ha finalizado. NO equivale a radicar (Draft→Submitted sigue exigiendo
    /// identidad + FUR + gates en <see cref="UseCases.ProcedureInstances.SubmitGate"/>).
    /// </summary>
    public DateTimeOffset? DraftFinalizedAt { get; set; }

    /// <summary>
    /// HU #10536 — el gestor marca el trámite como prioritario para que el OT lo revise con primacía.
    /// Solo afecta el ordenamiento de los listados (operación y bandeja del OT); NO altera el ciclo de
    /// vida ni los gates. Default false. Columna agregada por migración SQL cruda (tabla ExcludeFromMigrations).
    /// </summary>
    public bool Prioritario { get; set; }

    /// <summary>
    /// Subsanación activa sobre un trámite en <c>rechazado</c>: reabre la edición SIN cambiar el
    /// status de negocio. Al re-radicar (<c>rechazado → entregado</c>) se apaga. Columna por migración
    /// SQL cruda (tabla ExcludeFromMigrations).
    /// </summary>
    public bool SubsanacionActiva { get; set; }

    /// <summary>
    /// Cuántas veces se ha activado la subsanación en este expediente (contador monotónico).
    /// Columna por migración SQL cruda (tabla ExcludeFromMigrations).
    /// </summary>
    public int SubsanacionCount { get; set; }

    /// <summary>
    /// Snapshot de <c>field_values</c> capturado al activar la subsanación: el baseline contra el que
    /// se compara al re-radicar para decidir qué gates se re-evalúan.
    ///
    /// <para>Vive aquí y no en el historial de estados a propósito. Antes se guardaba en el
    /// <c>metadata</c> de una fila <c>rechazado → rechazado</c>, que no era una transición real y el
    /// timeline mostraba como un segundo rechazo. Columna por migración SQL cruda (tabla
    /// ExcludeFromMigrations).</para>
    /// </summary>
    public string? SubsanacionBaseline { get; set; }

    /// <summary>
    /// Feature #10701 / HU #10706 — marca de vigencia del expediente consolidado maestro. En
    /// <c>true</c> el <c>consolidado_maestro</c> persistido refleja el estado actual del expediente:
    /// el botón único "Ver consolidado" lo muestra tal cual (sin regenerar). Cualquier cambio
    /// importante —transición de estado (aprobar/rechazar/…) o adjuntar la Licencia de Tránsito—
    /// la baja a <c>false</c>, y la siguiente petición regenera el PDF antes de mostrarlo. Default
    /// false (un trámite sin consolidado vigente se genera al primer clic). Columna agregada por
    /// migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea al modelo EF.
    /// </summary>
    public bool ConsolidadoMaestroVigente { get; set; }

    /// <summary>
    /// Migración V1→V2 (traspasos) — marca de "trámite histórico importado desde Flit V1". En
    /// <c>true</c> el trámite es una FOTO de solo lectura: no se capturó paso a paso en V2, así que
    /// no debe someterse al gating del wizard vivo (que exige datos —comercial, biométrica, FUR—
    /// que la migración legítimamente no reconstruye). Cuando además está en estado terminal
    /// (<see cref="Tramites.Estados.TramiteEstado.EsFinal"/>), el wizard reporta el expediente como
    /// completo/solo-lectura para que el visor lo muestre íntegro (ver <c>GetWizardStateHandler.ComputeState</c>).
    /// Default false (trámite nativo de V2). Columna agregada por migración SQL cruda (la tabla está
    /// ExcludeFromMigrations); aquí solo se mapea al modelo EF. La setea el migrador Flit.DataMigration.V1.
    /// </summary>
    public bool IsMigrated { get; set; }

    /// <summary>
    /// Origen del trámite y referencia externa del sistema originador. Para los trámites materializados
    /// por la integración con terceros (ICT): <c>Origin='ict'</c> y <c>ExternalRef</c> = id del pre-trámite
    /// (<c>ict.external_integration_master.id</c>). Sostienen la materialización IDEMPOTENTE: el índice
    /// único parcial (tenant_id, external_ref) impide crear dos borradores para el mismo pre-trámite y el
    /// guard de <c>CreateDraftFromIct</c> reusa el existente ante un reintento. Null para trámites de
    /// plataforma. Columnas agregadas por migración SQL cruda (tabla ExcludeFromMigrations); aquí solo se mapean.
    /// </summary>
    public string? Origin { get; set; }

    public string? ExternalRef { get; set; }

    /// <summary>
    /// Pausa del trámite (ICT — servicio v1 <c>pauseDraftProcess</c> y bandera <c>starts_procedure_in_paused</c>
    /// del register). En <c>true</c> el trámite NO avanza: la radicación/preparación se bloquean
    /// (<see cref="UseCases.ProcedureInstances.SubmitProcedureInstanceHandler"/> devuelve
    /// <see cref="Tramites.Estados.TramiteEstadoErrores.TramitePausado"/>). Reversible (reanudar). La
    /// anulación NO se bloquea. Default false ⇒ trámites de plataforma nunca están pausados (sin impacto).
    /// Columna agregada por migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea al modelo EF.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// Nota mostrada en el dashboard cuando el trámite está pausado (origen ICT). Solo INFORMATIVA: no
    /// dispara lógica. Se limpia al reanudar. Columna cruda (tabla ExcludeFromMigrations); solo mapeo EF.
    /// </summary>
    public string? PausedObservation { get; set; }

    /// <summary>
    /// Vigencia del expediente derivado del wizard (FUR + documentos en caliente + consolidado),
    /// espejo de <see cref="ConsolidadoMaestroVigente"/> (HU #10860, Feature #10852, ADR-0032).
    /// <c>true</c> = el consolidado persistido refleja el expediente actual (se sirve sin regenerar);
    /// <c>false</c> = un cambio de estado, la decisión del OT o adjuntar la LT lo invalidó, y la
    /// próxima generación regenera en cascada el FUR y sus documentos en caliente (con fecha vigente)
    /// antes de consolidar. Default false. Columna agregada por migración SQL cruda (tabla
    /// ExcludeFromMigrations); aquí solo se mapea al modelo EF.
    /// </summary>
    public bool ConsolidadoWizardVigente { get; set; }

    /// <summary>
    /// Invalida los consolidados persistidos (maestro y wizard) tras un cambio que altera el
    /// expediente —transición de estado, decisión del OT o adjuntar la LT—: la próxima generación los
    /// regenera. HU #10860 (ADR-0032) — un único punto que mantiene ambos flags consistentes.
    /// </summary>
    public void InvalidarConsolidados()
    {
        ConsolidadoMaestroVigente = false;
        ConsolidadoWizardVigente = false;
    }

    /// <summary>
    /// ADR-0036 §D9 (HU #10916) — mandatario (<c>admin.mandate_signers</c>) que firma el mandato de este
    /// trámite, resuelto al APROBAR: automático si hay uno solo o el cotejo por usuario es único; elegido
    /// explícitamente si hay varios sin match. <c>null</c> mientras no se aprueba, si el mandato no aplica,
    /// o si el mandatario es institucional (Sabaneta UT-SETSA, sin firmante persona). <c>ON DELETE SET
    /// NULL</c>. Columna agregada por migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo
    /// se mapea al modelo EF.
    /// </summary>
    public Guid? MandateSignerId { get; set; }

    /// <summary>
    /// VIN denormalizado desde <c>procedure_instance_field_values</c> (field_key <c>vin</c>), mantenido
    /// por trigger de BD (migración TramitesCamposBusqueda). Habilita filtrar/ordenar el listado en SQL
    /// sin cargar el grafo completo. SOLO LECTURA para el aplicativo: la fuente de verdad sigue siendo
    /// <see cref="FieldValues"/>; escribir aquí directamente no se propaga a field_values. Columna
    /// agregada por migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea.
    /// </summary>
    public string? Vin { get; set; }

    /// <summary>Placa denormalizada (field_key <c>plate</c>). Mismo propósito y misma advertencia de
    /// solo-lectura que <see cref="Vin"/>.</summary>
    public string? Plate { get; set; }

    /// <summary>
    /// Nombre del vendedor (actor_type <c>vendedor</c>) denormalizado desde
    /// <see cref="Actors"/> por trigger. Null en matrícula inicial (no hay vendedor) o si el actor aún
    /// no se ha registrado. SOLO LECTURA para el aplicativo.
    /// </summary>
    public string? VendedorNombre { get; set; }

    /// <summary>Nombre del comprador (actor_type <c>comprador</c>) denormalizado desde
    /// <see cref="Actors"/> por el mismo trigger que <see cref="VendedorNombre"/>. SOLO LECTURA.</summary>
    public string? CompradorNombre { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? RulesSnapshotAt { get; set; }
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public ProcedureType? ProcedureType { get; set; }
    public ICollection<ProcedureInstanceActor> Actors { get; set; } = [];
    public ICollection<ProcedureInstanceFieldValue> FieldValues { get; set; } = [];
    public ICollection<ProcedureInstanceStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<ProcedureInstanceAttachment> Attachments { get; set; } = [];
    public ICollection<ProcedureInstancePreflightSnapshot> PreflightSnapshots { get; set; } = [];
    public ICollection<ProcedureInstanceEvent> Events { get; set; } = [];
    public ICollection<ProcedureInstanceBiometricValidation> BiometricValidations { get; set; } = [];
    public ICollection<ProcedureInstanceSignature> Signatures { get; set; } = [];
    public ICollection<ProcedureInstanceParticipant> Participants { get; set; } = [];
    public ProcedureInstanceCommercial? Commercial { get; set; }
}
