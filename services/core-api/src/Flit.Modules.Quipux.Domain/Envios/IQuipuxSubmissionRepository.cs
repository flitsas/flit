using Flit.Modules.Quipux.Domain.Trazabilidad;

namespace Flit.Modules.Quipux.Domain.Envios;

/// <summary>
/// Acceso a las radicaciones Quipux. El claim se hace con <c>FOR UPDATE SKIP LOCKED</c> (SQL crudo,
/// EF no expone el locking clause) para que la ventana de redeploy —o un futuro escalado a N
/// réplicas— no procese la misma fila dos veces. FLIT 1.0 no tenía nada de esto: dos ejecuciones
/// solapadas radicaban el mismo trámite dos veces en Quipux.
/// </summary>
public interface IQuipuxSubmissionRepository
{
    /// <summary>
    /// Reclama una submission pendiente con <c>attempts &lt; maxAttempts</c> cuyo lease esté vacío
    /// o vencido (<c>claimed_at IS NULL OR claimed_at &lt; leaseCutoff</c>), sellando
    /// <c>claimed_at</c> e incrementando <c>attempts</c> ANTES de procesar (si el proceso muere,
    /// el intento igual cuenta). Devuelve null cuando no queda nada reclamable.
    /// </summary>
    /// <param name="leaseCutoff">
    /// Momento antes del cual un lease se considera abandonado (worker muerto a medio ciclo).
    /// Debe ser holgadamente mayor que el timeout HTTP de Quipux: un lease demasiado corto
    /// reabriría la fila mientras el POST sigue en vuelo y volvería a duplicar la radicación.
    /// </param>
    Task<QuipuxSubmission?> ClaimNextPendienteAsync(
        int maxAttempts,
        DateTimeOffset leaseCutoff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclama una submission registrada cuya última consulta sea anterior a
    /// <paramref name="stalenessCutoff"/> y con <c>poll_count &lt; maxPolls</c>. La ventana de
    /// frescura evita re-sondear la misma fila en ciclos seguidos.
    /// </summary>
    Task<QuipuxSubmission?> ClaimNextRegistradaAsync(
        DateTimeOffset stalenessCutoff,
        int maxPolls,
        CancellationToken cancellationToken = default);

    Task<QuipuxSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encola una radicación. Idempotente: si el trámite ya tiene una submission activa devuelve
    /// la existente sin insertar. El chequeo previo es deliberado — el índice único parcial
    /// <c>uq_quipux_submissions_activa</c> es la red de seguridad, pero llegar a violarlo lanzaría
    /// un 23505 que, al compartir <c>SaveChanges</c>, abortaría el caso de uso completo (la misma
    /// trampa documentada en <c>IdentityValidationOutboxWriter</c>).
    /// </summary>
    Task<QuipuxSubmission> EnqueueIfAbsentAsync(
        QuipuxSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>Persiste los cambios de la submission y su evento de bitácora en un solo <c>SaveChanges</c>.</summary>
    Task UpdateAsync(
        QuipuxSubmission submission,
        QuipuxSubmissionEvent? auditEvent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trámites elegibles aún sin submission: en estado <c>preparado</c>, cuyo tipo tiene mapeo
    /// Quipux (<c>procedure_types.external_refs-&gt;'quipux'</c>), y cuya SECRETARÍA DESTINO tiene
    /// <c>code_divipo</c> y la bandera encendida para la familia de ese trámite
    /// (<c>quipux_registration</c> / <c>quipux_transfer</c> / <c>quipux_other</c>).
    /// </summary>
    /// <remarks>
    /// Es el equivalente 2.0 del <c>listQuipux</c> de FLIT 1.0, que filtraba por
    /// <c>traffic_secretaries.id_parinttrasec_transfer = 2</c> — o sea, por una bandera del
    /// CATÁLOGO de secretarías, igual que aquí. No se filtra por
    /// <c>admin.transit_office_profiles.operation_mode</c>: ese perfil describe al OT-cliente cuya
    /// consola queda en solo lectura, no al destino al que se radica.
    /// </remarks>
    Task<IReadOnlyList<QuipuxTramiteElegible>> ListElegiblesSinSubmissionAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Trámite candidato a radicarse. Trae el tenant además del id porque el barrido es cross-tenant
/// (el worker no tiene tenant en contexto) y encolar lo exige: resolverlo después obligaría a una
/// segunda lectura por cada trámite, y el <c>tenant_id</c> ya está en la fila que se acaba de leer.
/// </summary>
public sealed record QuipuxTramiteElegible(Guid ProcedureInstanceId, Guid TenantId);
