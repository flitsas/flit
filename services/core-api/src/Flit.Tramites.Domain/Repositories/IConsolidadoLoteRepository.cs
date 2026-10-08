using System.Net;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Épica #13216 (HU #13373, ADR-0070 D8) — persistencia del ciclo de vida «crear y retener» de un lote de descarga
/// masiva de consolidados. Dueño: HU #13373. El cierre de cada ítem NO vive aquí (#13375 tiene su propio puerto); el
/// reclamo con lease del carril de ítems sí (HU #13376).
/// <list type="bullet">
///   <item><see cref="CrearAsync"/>: en UNA transacción purga el lote retenido del usuario, inserta el lote con su
///   DEK envuelta, sus ítems (COPY binario) y la auditoría <c>lote_creado</c>. Si algo falla no queda nada.</item>
///   <item><see cref="PurgarAsync"/>: borrado criptográfico de un lote terminal (la misma operación de dominio
///   <see cref="ConsolidadoLotePurga"/> que usa la creación); la reutiliza la purga a las 24 h (#13379).</item>
/// </list>
/// Nunca expone <c>PostgresException</c>: el 23505 sobre <c>uq_consolidado_export_batches_active_per_user</c> se
/// traduce a <see cref="CrearLoteEstado.LoteActivo"/> y cualquier otro fallo a <see cref="CrearLoteEstado.NoCreado"/>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await repo.CrearAsync(nuevo, ct);
/// if (r.Estado == CrearLoteEstado.LoteActivo) return Conflict(r.LoteActivoId);
/// </code>
/// </remarks>
public interface IConsolidadoLoteRepository
{
    /// <summary>Parámetros del motor (fila única). <c>null</c> si la fila no existe: el llamador lo trata como apagado.</summary>
    Task<ConsolidadoExportSettings?> ObtenerSettingsAsync(CancellationToken ct = default);

    /// <summary>Id del lote activo (<c>en_cola</c>, <c>en_proceso</c>, <c>empaquetando</c>) del usuario, o <c>null</c>.</summary>
    Task<Guid?> ObtenerLoteActivoIdAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Crea el lote en una sola transacción: purga del lote retenido del usuario (H10a, CF-12) + lote <c>en_cola</c> +
    /// ítems <c>pendiente</c> en el orden de <see cref="NuevoLoteConsolidados.Items"/> + auditoría <c>lote_creado</c>.
    /// </summary>
    Task<CrearLoteResultado> CrearAsync(NuevoLoteConsolidados nuevo, CancellationToken ct = default);

    /// <summary>
    /// Purga un lote terminal no purgado en su propia transacción (con lock del lote): DEK a <c>NULL</c>, partes
    /// cerradas a <c>purgada</c>, lote <c>expirado</c> y auditoría <c>lote_purgado</c>. Devuelve <c>false</c> si el
    /// lote no existe, está activo o ya estaba purgado. No toca <c>items.plate</c> (decisión S2 = b).
    /// </summary>
    Task<bool> PurgarAsync(Guid loteId, DateTimeOffset ahora, CancellationToken ct = default);

    /// <summary>
    /// HU #13376 (ADR-0070 D2, CF-20/CF-21) — reclama UN ítem del carril en una sola sentencia atómica:
    /// <list type="bullet">
    ///   <item>Elige el lote <c>en_cola</c>/<c>en_proceso</c> con <c>last_claimed_at</c> más antiguo (nulo primero) que
    ///   tenga un ítem reclamable, y le toma <b>un</b> ítem por <c>position</c> (equidad entre lotes).</item>
    ///   <item>Reclamable: <c>pendiente</c> con <c>next_attempt_at</c> vencido, o <c>procesando</c> con el lease
    ///   vencido (la ejecución anterior murió sin cerrar).</item>
    ///   <item>El ítem pasa a <c>procesando</c> con <c>lease_until = now() + leaseSegundos</c>; el lote sella
    ///   <c>last_claimed_at</c> y pasa de <c>en_cola</c> a <c>en_proceso</c> (con <c>started_at</c>).</item>
    /// </list>
    /// <para><b>Intentos:</b> el reclamo de un <c>pendiente</c> no toca <c>attempts</c> (contrato de #13375). Solo el
    /// re-reclamo de un <c>procesando</c> con lease vencido suma 1: esa ejecución terminó sin cierre (caída del proceso),
    /// y sin contarla un trámite que tumba el proceso se reintentaría sin fin.</para>
    /// Locks <c>FOR UPDATE SKIP LOCKED</c> en el orden lote → ítem (el mismo del cierre y de la cancelación).
    /// </summary>
    /// <returns>El lote (ya actualizado) y el ítem reclamado, o <c>null</c> si no hay nada reclamable.</returns>
    Task<ItemLoteReclamado?> ReclamarSiguienteItemAsync(string reclamante, int leaseSegundos, CancellationToken ct = default);

    /// <summary>
    /// HU #13376 — pasa a <c>en_proceso</c> los lotes <c>en_cola</c> sin ningún ítem (selección vacía): no tienen nada
    /// que reclamar y sin esto quedarían colgados. A partir de ahí los recoge el cierre del lote (#13377) por
    /// <see cref="ObtenerLotesConCarrilTerminadoAsync"/> y <see cref="CerrarCarrilAsync"/>. Devuelve cuántos lotes pasó.
    /// </summary>
    Task<int> IniciarLotesSinItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// HU #13376 — lotes <c>en_proceso</c> cuyo carril de ítems terminó: ningún ítem <c>pendiente</c> ni
    /// <c>procesando</c> (incluye el lote sin ítems). Es la señal con la que #13377 pasa el lote a empaquetar.
    /// </summary>
    Task<IReadOnlyList<Guid>> ObtenerLotesConCarrilTerminadoAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// HU #13377 (diseño §3, CF-09/CF-10) — cierra el carril de ítems de un lote en UNA transacción con
    /// <c>SELECT … FOR UPDATE</c> del lote (orden lote → ítem):
    /// <list type="bullet">
    ///   <item>Solo procede si el lote sigue <c>en_proceso</c>, sin borrado lógico y sin ítems <c>pendiente</c> ni
    ///   <c>procesando</c> (un lote cancelado o ya cerrado no se toca).</item>
    ///   <item>Asigna la última parte con todo lo que quede sin parte (PDF y omitidos) según <c>max_pdfs_per_part</c> y
    ///   <c>max_mb_per_part</c>; si el lote no tenía ninguna parte y todo fue omitido (o no tenía ítems), crea una
    ///   única parte solo con <c>omitidos.csv</c>.</item>
    ///   <item>Las partes nuevas quedan <c>pendiente</c> con <c>pdf_count</c>/<c>omitted_count</c>, numeradas sin huecos
    ///   a continuación de las existentes; los ítems quedan con su <c>part_number</c>; el lote pasa a
    ///   <c>empaquetando</c> con <c>parts_count</c> = número de partes.</item>
    /// </list>
    /// Sin fila de parámetros no hace nada (motor apagado).
    /// </summary>
    Task<CierreCarrilResultado> CerrarCarrilAsync(Guid loteId, CancellationToken ct = default);

    /// <summary>
    /// HU #13385 (diseño 09 §2.4 pasos 1–5, CF-09/CF-10) — cancela el lote del dueño en UNA transacción (estrategia de
    /// reintentos del contexto) con <c>SELECT … FOR UPDATE</c> del lote filtrado por <c>requested_by_user_id</c> (orden de
    /// locks lote → ítem/parte, el mismo del reclamo, los cierres, el empaquetado y la purga):
    /// <list type="bullet">
    ///   <item>No existe, borrado o de otro usuario (incluido un Super Admin con un lote ajeno) →
    ///   <see cref="CancelarLoteEstado.NoEncontrado"/>.</item>
    ///   <item>Ya <c>cancelado</c> → <see cref="CancelarLoteEstado.YaCancelado"/> con el lote tal cual (sin auditoría).</item>
    ///   <item>Otro terminal → <see cref="CancelarLoteEstado.Terminado"/> con el lote (no cambia nada).</item>
    ///   <item>Activo → ítems <c>pendiente</c>/<c>procesando</c> a <c>cancelado</c> (por bloques), partes y lote según
    ///   <see cref="ConsolidadoLoteCancelacion"/> y la fila <c>lote_cancelado</c>; si cualquier escritura falla, la
    ///   transacción se revierte entera → <see cref="CancelarLoteEstado.NoRegistrado"/>.</item>
    /// </list>
    /// Nunca expone <c>PostgresException</c>. No borra binarios: devuelve sus rutas para el borrado best-effort.
    /// </summary>
    Task<CancelarLoteResultado> CancelarAsync(CancelacionLote solicitud, CancellationToken ct = default);

    /// <summary>
    /// HU #13386 (diseño 09 §2.4, CF-09) — estado del lote para los checkpoints cooperativos de los carriles: lectura de
    /// UNA fila por clave primaria, sin lock y sin transacción (no compite con la cancelación ni con los cierres). Devuelve
    /// <c>null</c> si el lote no existe o tiene borrado lógico. Es una foto: la decisión firme la toman los cierres
    /// condicionados bajo el lock del lote; el checkpoint solo evita trabajo inútil (entregar, empaquetar, subir).
    /// </summary>
    Task<string?> GetStatusAsync(Guid batchId, CancellationToken ct = default);
}

/// <summary>Petición de cancelación (HU #13385). El dueño sale del token (<c>sub</c>), nunca del cliente.</summary>
/// <param name="LoteId">Lote a cancelar.</param>
/// <param name="UsuarioId">Solicitante (<c>sub</c>).</param>
/// <param name="RolCodigo">Rol con el que se pide (auditoría).</param>
/// <param name="ClientIp">IP del cliente (auditoría).</param>
/// <param name="UserAgent">Navegador, ya truncado (auditoría).</param>
public sealed record CancelacionLote(Guid LoteId, Guid UsuarioId, string RolCodigo, IPAddress? ClientIp = null, string? UserAgent = null);

/// <summary>Resultado de <see cref="IConsolidadoLoteRepository.CancelarAsync"/>.</summary>
public enum CancelarLoteEstado
{
    /// <summary>Lote, ítems, partes y <c>lote_cancelado</c> confirmados (202).</summary>
    Cancelado,

    /// <summary>Ya estaba cancelado: idempotente, sin nueva auditoría (202).</summary>
    YaCancelado,

    /// <summary>No existe o no es del usuario (404).</summary>
    NoEncontrado,

    /// <summary>Ya terminó en <c>completado</c>, <c>completado_con_omitidos</c>, <c>fallido</c> o <c>expirado</c> (409).</summary>
    Terminado,

    /// <summary>La transacción falló (p. ej. la auditoría): no cambió nada y el lote sigue activo (503).</summary>
    NoRegistrado,
}

/// <param name="Estado">Qué pasó.</param>
/// <param name="Lote">El lote tras la operación (o tal cual en <see cref="CancelarLoteEstado.YaCancelado"/> y
/// <see cref="CancelarLoteEstado.Terminado"/>); <c>null</c> en <see cref="CancelarLoteEstado.NoEncontrado"/> y
/// <see cref="CancelarLoteEstado.NoRegistrado"/>.</param>
/// <param name="ItemsCancelados">Ítems vivos que pasaron a <c>cancelado</c>.</param>
/// <param name="RutasPartesPurgadas"><c>storage_path</c> de las partes que pasaron a <c>purgada</c>.</param>
public sealed record CancelarLoteResultado(
    CancelarLoteEstado Estado,
    ConsolidadoExportBatch? Lote = null,
    int ItemsCancelados = 0,
    IReadOnlyList<string>? RutasPartesPurgadas = null);

/// <summary>Resultado de <see cref="IConsolidadoLoteRepository.CerrarCarrilAsync"/>.</summary>
/// <param name="Aplicado"><c>true</c> si el lote pasó a <c>empaquetando</c>.</param>
/// <param name="PartesCreadas">Partes creadas en este cierre.</param>
/// <param name="PartesTotales">Partes del lote tras el cierre (<c>parts_count</c>).</param>
public sealed record CierreCarrilResultado(bool Aplicado, int PartesCreadas, int PartesTotales)
{
    /// <summary>El lote no estaba en condiciones de cerrar el carril (cancelado, ya cerrado, ítems vivos, motor sin parámetros).</summary>
    public static CierreCarrilResultado NoAplicado { get; } = new(false, 0, 0);
}

/// <summary>Resultado de <see cref="IConsolidadoLoteRepository.ReclamarSiguienteItemAsync"/>.</summary>
/// <param name="Lote">Lote del ítem tras el reclamo (<c>en_proceso</c>).</param>
/// <param name="Item">Ítem en <c>procesando</c> con su lease.</param>
public sealed record ItemLoteReclamado(ConsolidadoExportBatch Lote, ConsolidadoExportBatchItem Item);

/// <summary>
/// Datos de un lote nuevo, ya validados y resueltos por el caso de uso. Las reglas de origen ↔ compañía ↔ organismo
/// las garantiza además el DDL 133 (CHECK bicondicionales).
/// </summary>
public sealed record NuevoLoteConsolidados
{
    /// <summary>Compañía del solicitante; <c>null</c> si y solo si <see cref="Origen"/> es superadmin.</summary>
    public Guid? TenantId { get; init; }

    public required Guid UsuarioId { get; init; }

    public required string RolCodigo { get; init; }

    /// <summary>
    /// Compañía a la que se acotó la selección: origen superadmin (<c>X-Tenant-Id</c>) o, con <see cref="NetworkScope"/>,
    /// la hija acotada del lote de red (HU #13417).
    /// </summary>
    public Guid? ScopeTenantId { get; init; }

    /// <summary>HU #13417 — lote creado desde la vista de red (<c>batches.network_scope</c>); solo origen tramites.</summary>
    public bool NetworkScope { get; init; }

    /// <summary>Uno de <see cref="ConsolidadoExportOrigin"/>.</summary>
    public required string Origen { get; init; }

    /// <summary>Uno de <see cref="ConsolidadoExportDocumentType"/>.</summary>
    public required string TipoDocumento { get; init; }

    /// <summary>Uno de <see cref="ConsolidadoExportSelectionMode"/>.</summary>
    public required string ModoSeleccion { get; init; }

    /// <summary>Solo origen ot_bandeja: organismo fijado.</summary>
    public Guid? OtTransitOfficeId { get; init; }

    /// <summary>DEK del lote ya envuelta (<c>IConsolidadoLoteCipher.GenerarDekEnvuelta</c>). Nunca la DEK en claro.</summary>
    public required byte[] DekEnvuelta { get; init; }

    public required DateTimeOffset EfectosAceptadosEn { get; init; }

    /// <summary>Selección congelada, sin duplicados, en el orden del listado del origen.</summary>
    public required IReadOnlyList<ProcedureInstanceRef> Items { get; init; }

    /// <summary>Filtro minimizado (JSON) para <c>filter_summary</c>; nunca valores de listas pegadas ni texto libre.</summary>
    public string? ResumenFiltroJson { get; init; }

    public int? IdsCount { get; init; }

    public int? ExcluidosCount { get; init; }

    public IPAddress? ClientIp { get; init; }

    public string? UserAgent { get; init; }
}

/// <summary>Resultado de <see cref="IConsolidadoLoteRepository.CrearAsync"/>.</summary>
public enum CrearLoteEstado
{
    /// <summary>Lote, ítems y auditoría confirmados.</summary>
    Creado,

    /// <summary>El usuario ya tenía un lote activo (consulta previa o 23505 del índice único parcial).</summary>
    LoteActivo,

    /// <summary>La transacción falló (p. ej. la auditoría no se pudo escribir): no quedó ninguna fila.</summary>
    NoCreado,
}

/// <param name="Estado">Qué pasó.</param>
/// <param name="Lote">El lote creado (solo <see cref="CrearLoteEstado.Creado"/>).</param>
/// <param name="LoteActivoId">El lote activo que impidió crear (solo <see cref="CrearLoteEstado.LoteActivo"/>).</param>
/// <param name="LotesPurgados">Lotes retenidos del usuario purgados en la misma transacción.</param>
public sealed record CrearLoteResultado(
    CrearLoteEstado Estado,
    ConsolidadoExportBatch? Lote = null,
    Guid? LoteActivoId = null,
    int LotesPurgados = 0);
