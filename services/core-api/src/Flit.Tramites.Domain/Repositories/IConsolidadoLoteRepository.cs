using System.Net;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Épica #13216 (HU #13373, ADR-0070 D8) — persistencia del ciclo de vida «crear y retener» de un lote de descarga
/// masiva de consolidados. Dueño: HU #13373. El proceso de ítems, el reclamo y el lease NO viven aquí (#13375/#13376
/// tienen su propio puerto).
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
}

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

    /// <summary>Solo origen superadmin: compañía a la que se acotó la selección.</summary>
    public Guid? ScopeTenantId { get; init; }

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
