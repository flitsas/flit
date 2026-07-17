using System.Data.Common;
using Flit.Admin.Domain.OtProfile;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de radicaciones Quipux (<c>tramites.quipux_submissions</c>).
///
/// <para><b>Reclamo con <c>FOR UPDATE SKIP LOCKED</c>.</b> Los dos <c>Claim*</c> usan SQL crudo porque
/// EF Core no expone el locking clause en LINQ. Hoy core-api corre con UNA réplica, pero el reclamo
/// exclusivo NO es opcional: <c>docker compose up -d</c> (sin <c>--wait</c>) solapa el contenedor
/// viejo y el nuevo durante el redeploy, y en esa ventana hay dos workers vivos. FLIT 1.0 no tenía
/// nada de esto y dos ejecuciones solapadas del cron radicaban el mismo trámite dos veces en Quipux.
/// Es el mismo patrón de <c>IdentityValidationOutboxProcessor</c> /
/// <c>ProcedureStateChangeOutboxProcessor</c> / <c>IdentityValidationReconcileProcessor</c>.</para>
///
/// <para><b>RLS y acceso cross-tenant.</b> <c>tramites.quipux_submissions</c> y
/// <c>quipux_submission_events</c> llevan la política <c>tenant_isolation</c> sobre el GUC
/// <c>app.current_tenant_id</c>, igual que el resto del schema <c>tramites</c>. Este repositorio lo
/// consume un BackgroundService que NO tiene tenant en contexto: barre TODOS los tenants. Se sigue
/// EXACTAMENTE el enfoque ya vigente en el repo para workers cross-tenant sobre tablas de
/// <c>tramites</c> con RLS (<c>IdentityValidationOutboxProcessor</c> y
/// <c>ProcedureStateChangeOutboxProcessor</c>, ambos sobre outboxes con <c>tenant_isolation</c>): NO
/// se fija el GUC ni se toca <c>row_security</c>. El rol de conexión de core-api es el propietario de
/// las tablas y éstas no declaran <c>FORCE ROW LEVEL SECURITY</c>, así que la política no se le aplica
/// — está documentado tal cual en <c>AnalyticsReadRepository</c> para la vista global de SuperAdmin.
/// Ojo: <c>SET LOCAL row_security = off</c> NO habría servido como "bypass" — en Postgres es una
/// aserción, no un permiso: para un rol sin BYPASSRLS hace FALLAR la consulta en vez de saltarse la
/// política; para el propietario es un no-op. Por eso no se usa aquí (en los seeds sí aparece, pero
/// allí corre como propietario y es exactamente ese no-op defensivo).
/// El aislamiento en este repositorio se sostiene con filtros EXPLÍCITOS: el
/// <c>tenant_id</c> viaja en cada fila y en cada evento de bitácora, y las consultas por trámite
/// filtran por <c>procedure_instance_id</c>, nunca por "todo lo visible".</para>
///
/// <para><b>Rama InMemory.</b> Los tests usan EF InMemory, donde el SQL crudo y las transacciones
/// explotan; por eso cada <c>Claim*</c> tiene un camino LINQ equivalente tras
/// <c>!db.Database.IsRelational()</c>, igual que <c>ProcedureStateChangeOutboxProcessor</c>.</para>
/// </summary>
internal sealed class QuipuxSubmissionRepository(FlitDbContext db) : IQuipuxSubmissionRepository
{
    /// <summary>
    /// Reclama la submission <c>pendiente</c> más antigua con <c>attempts &lt; maxAttempts</c>.
    ///
    /// <para>El <c>ORDER BY created_at</c> no es estético: el índice parcial
    /// <c>ix_quipux_submissions_claim_pendiente</c> está definido sobre <c>(created_at) WHERE
    /// status='pendiente'</c>, así que cualquier otro orden lo dejaría inservible.</para>
    ///
    /// <para><c>attempts</c> se incrementa ANTES de devolver la fila y se confirma con el commit del
    /// reclamo: si el proceso muere a mitad de la radicación, el intento igual cuenta. Es la regla del
    /// patrón outbox del repo — el coste de perder un intento es un reintento menos; el de no contarlo
    /// es un veneno reintentado por siempre.</para>
    /// </summary>
    public async Task<QuipuxSubmission?> ClaimNextPendienteAsync(
        int maxAttempts,
        DateTimeOffset leaseCutoff,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
            return await ClaimPendienteInMemoryAsync(maxAttempts, leaseCutoff, cancellationToken).ConfigureAwait(false);

        // El DbContext usa EnableRetryOnFailure → la transacción manual DEBE ir dentro de la execution
        // strategy (reintenta el bloque completo ante fallos transitorios de BD). Sin esto, EF lanza.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy
            .ExecuteAsync(async () => await ClaimPendienteRelationalAsync(maxAttempts, leaseCutoff, cancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    private async Task<QuipuxSubmission?> ClaimPendienteRelationalAsync(
        int maxAttempts,
        DateTimeOffset leaseCutoff,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // estado limpio en cada (re)intento de la strategy
        var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // El lease (claimed_at) es lo que impide radicar dos veces. SKIP LOCKED solo hace
            // exclusivo el instante del reclamo: esta transacción commitea antes del POST a Quipux
            // —que es lento— y sin el sello la fila volvería a ser reclamable de inmediato. En la
            // ventana de redeploy (el CD solapa contenedor viejo y nuevo) eso radicaría el mismo
            // documento dos veces en la secretaría, que es justo el defecto de FLIT 1.0.
            var claimedId = await ClaimIdAsync(
                db,
                """
                SELECT id
                FROM tramites.quipux_submissions
                WHERE status = @status
                  AND attempts < @max_attempts
                  AND (claimed_at IS NULL OR claimed_at < @lease_cutoff)
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """,
                cmd =>
                {
                    AddParam(cmd, "status", QuipuxSubmissionEstado.Pendiente);
                    AddParam(cmd, "max_attempts", maxAttempts);
                    AddParam(cmd, "lease_cutoff", leaseCutoff);
                },
                ct).ConfigureAwait(false);

            if (claimedId is null)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return null;
            }

            // La fila está bloqueada por esta transacción (FOR UPDATE); se carga rastreada para marcarla.
            var row = await db.QuipuxSubmissions
                .FirstAsync(s => s.Id == claimedId.Value, ct).ConfigureAwait(false);

            MarcarIntento(row);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return row;
        }
    }

    private async Task<QuipuxSubmission?> ClaimPendienteInMemoryAsync(
        int maxAttempts,
        DateTimeOffset leaseCutoff,
        CancellationToken ct)
    {
        // Tests (InMemory): sin transacciones ni locking — reclamo directo por LINQ. El filtro de
        // lease sí se replica: es regla de negocio, no un detalle del motor.
        var row = await db.QuipuxSubmissions
            .Where(s => s.Status == QuipuxSubmissionEstado.Pendiente
                && s.Attempts < maxAttempts
                && (s.ClaimedAt == null || s.ClaimedAt < leaseCutoff))
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
            return null;

        MarcarIntento(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <summary>
    /// Reclama la submission <c>registrado</c> menos fresca cuyo último sondeo
    /// (<c>last_polled_at</c>, o <c>created_at</c> si nunca se sondeó) sea anterior a
    /// <paramref name="stalenessCutoff"/> y con <c>poll_count &lt; maxPolls</c>.
    ///
    /// <para>La ventana de frescura + el estampado de <c>last_polled_at</c> AL RECLAMAR son lo que evita
    /// re-sondear la misma fila en ciclos seguidos: al confirmarse el reclamo la fila deja de cumplir el
    /// predicado hasta que pase la ventana. Es el patrón de <c>IdentityValidationReconcileProcessor</c>
    /// (<c>COALESCE(updated_at, created_at) &lt; @cutoff</c> + rate-limit por presupuesto de sondeos).</para>
    ///
    /// <para><c>poll_count</c> acota el sondeo indefinido de un trámite que Quipux nunca resuelve —el
    /// equivalente al presupuesto de reconciliación de identidad—.</para>
    /// </summary>
    public async Task<QuipuxSubmission?> ClaimNextRegistradaAsync(
        DateTimeOffset stalenessCutoff,
        int maxPolls,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
            return await ClaimRegistradaInMemoryAsync(stalenessCutoff, maxPolls, cancellationToken)
                .ConfigureAwait(false);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy
            .ExecuteAsync(async () =>
                await ClaimRegistradaRelationalAsync(stalenessCutoff, maxPolls, cancellationToken)
                    .ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    private async Task<QuipuxSubmission?> ClaimRegistradaRelationalAsync(
        DateTimeOffset stalenessCutoff, int maxPolls, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var claimedId = await ClaimIdAsync(
                db,
                // COALESCE(last_polled_at, created_at): una submission recién registrada nunca se ha
                // sondeado (last_polled_at NULL) y debe entrar a la cola por su antigüedad, no quedarse
                // fuera. El índice ix_quipux_submissions_claim_registrada (last_polled_at) WHERE
                // status='registrado' acota el escaneo al subconjunto vivo.
                """
                SELECT id
                FROM tramites.quipux_submissions
                WHERE status = @status
                  AND poll_count < @max_polls
                  AND COALESCE(last_polled_at, created_at) < @cutoff
                ORDER BY COALESCE(last_polled_at, created_at)
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """,
                cmd =>
                {
                    AddParam(cmd, "status", QuipuxSubmissionEstado.Registrado);
                    AddParam(cmd, "max_polls", maxPolls);
                    AddParam(cmd, "cutoff", stalenessCutoff);
                },
                ct).ConfigureAwait(false);

            if (claimedId is null)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return null;
            }

            var row = await db.QuipuxSubmissions
                .FirstAsync(s => s.Id == claimedId.Value, ct).ConfigureAwait(false);

            MarcarSondeo(row);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return row;
        }
    }

    private async Task<QuipuxSubmission?> ClaimRegistradaInMemoryAsync(
        DateTimeOffset stalenessCutoff, int maxPolls, CancellationToken ct)
    {
        var row = await db.QuipuxSubmissions
            .Where(s => s.Status == QuipuxSubmissionEstado.Registrado
                && s.PollCount < maxPolls
                && (s.LastPolledAt ?? s.CreatedAt) < stalenessCutoff)
            .OrderBy(s => s.LastPolledAt ?? s.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
            return null;

        MarcarSondeo(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <summary>Submission por id. Rastreada: quien la lee suele terminar pasándola a <see cref="UpdateAsync"/>.</summary>
    public async Task<QuipuxSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.QuipuxSubmissions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Encola una radicación de forma IDEMPOTENTE: si el trámite ya tiene una submission activa
    /// (<c>pendiente</c> | <c>registrado</c>) devuelve la existente SIN insertar.
    ///
    /// <para>El chequeo previo es deliberado y NO es redundante con
    /// <c>uq_quipux_submissions_activa</c>: ese índice es la red de seguridad de la BD, pero llegar a
    /// violarlo lanzaría un 23505 que, al compartir el <c>SaveChanges</c> del caso de uso, abortaría la
    /// unidad de trabajo ENTERA (estado del trámite + historial + submission), dejando el trámite
    /// atascado en un 500. Es exactamente la trampa documentada en
    /// <c>IdentityValidationOutboxWriter</c>, y por eso se mira el ChangeTracker ADEMÁS de la BD: dos
    /// encolados dentro del mismo caso de uso no habrían tocado aún la tabla.</para>
    ///
    /// <para>Sin <c>SaveChanges</c>: la fila se confirma con la unidad de trabajo del caso de uso
    /// (mismo contrato transaccional que el writer de la outbox de identidad).</para>
    /// </summary>
    public async Task<QuipuxSubmission> EnqueueIfAbsentAsync(
        QuipuxSubmission submission,
        CancellationToken cancellationToken = default)
    {
        // (1) ChangeTracker: encolados de este mismo caso de uso, todavía sin llegar a la tabla.
        var pendienteEnTracker = db.ChangeTracker
            .Entries<QuipuxSubmission>()
            .Where(e => e.State is not EntityState.Deleted
                && e.Entity.ProcedureInstanceId == submission.ProcedureInstanceId
                && !ReferenceEquals(e.Entity, submission)
                && EsActiva(e.Entity.Status))
            .Select(e => e.Entity)
            .FirstOrDefault();

        if (pendienteEnTracker is not null)
            return pendienteEnTracker;

        // (2) BD: radicación viva de un ciclo anterior (o de otro proceso).
        var existente = await db.QuipuxSubmissions
            .FirstOrDefaultAsync(
                s => s.ProcedureInstanceId == submission.ProcedureInstanceId
                    && (s.Status == QuipuxSubmissionEstado.Pendiente
                        || s.Status == QuipuxSubmissionEstado.Registrado),
                cancellationToken)
            .ConfigureAwait(false);

        if (existente is not null)
            return existente;

        if (submission.Id == Guid.Empty)
            submission.Id = Guid.NewGuid();
        if (submission.CreatedAt == default)
            submission.CreatedAt = DateTimeOffset.UtcNow;

        // Added explícito: la PK es store-generated (uuidv7()) pero se asigna en código.
        db.Add(submission);
        return submission;
    }

    /// <summary>
    /// Persiste la submission y su evento de bitácora en UN SOLO <c>SaveChanges</c> (EF lo envuelve en
    /// una transacción implícita ⇒ ambos o ninguno).
    ///
    /// <para>Esta atomicidad es el fix del bug más grave de FLIT 1.0: allí el estado se persistía antes
    /// que el log y sin transacción, así que un fallo entre medias dejaba el trámite en estado 5 sin su
    /// log 81 — y la query de seguimiento exigía ese log vía INNER JOIN, o sea que el trámite no se
    /// volvía a consultar NUNCA. Un trámite huérfano, invisible, para siempre.</para>
    /// </summary>
    public async Task UpdateAsync(
        QuipuxSubmission submission,
        QuipuxSubmissionEvent? auditEvent = null,
        CancellationToken cancellationToken = default)
    {
        submission.UpdatedAt = DateTimeOffset.UtcNow;

        // Normalmente la submission viene rastreada (del claim o de GetById); si el llamador la trae
        // desconectada, se adjunta para que el UPDATE salga en el mismo SaveChanges que el evento.
        if (db.Entry(submission).State is EntityState.Detached)
            db.QuipuxSubmissions.Update(submission);

        if (auditEvent is not null)
        {
            if (auditEvent.Id == Guid.Empty)
                auditEvent.Id = Guid.NewGuid();
            if (auditEvent.OccurredAt == default)
                auditEvent.OccurredAt = DateTimeOffset.UtcNow;

            db.Add(auditEvent);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ids de trámites elegibles para radicar y que aún NO tienen submission activa. Equivalente 2.0 del
    /// <c>listQuipux</c> de 1.0 (que filtraba <c>id_parinttrasec_transfer = 2</c>).
    ///
    /// <para>Elegible = <c>preparado</c> + OT del trámite en modo <c>quipux</c> +
    /// <c>procedure_types.external_refs-&gt;'quipux'</c> presente (mapeo por datos: añadir un trámite
    /// nuevo es un UPDATE, no un deploy) + OT con <c>codigoDivipo</c> en sus <c>external_refs</c>. Sin
    /// esos dos últimos el registro contra Quipux fallaría con datos incompletos: mejor no reclamarlo
    /// que producir errores.</para>
    ///
    /// <para><b>SQL crudo</b> porque <c>external_refs</c> de <c>catalogs.transit_offices</c> no está
    /// mapeada en EF (la entidad <c>TransitOffice</c> no la expone), así que el predicado no es
    /// expresable en LINQ. Se usa <c>-&gt; 'quipux' IS NOT NULL</c> en vez del operador <c>?</c> de
    /// jsonb a propósito: <c>?</c> colisiona con el marcador de parámetro y Npgsql lo malinterpreta.</para>
    ///
    /// <para><b>SIEMPRE con LIMIT.</b> Las tres queries de 1.0 no paginaban y traían todo a memoria; el
    /// "límite de 20" se aplicaba después, en el cliente. Aquí el tope viaja al motor.</para>
    ///
    /// <para><b>EXISTS sobre <c>transit_office_profiles</c>, no JOIN.</b> Esa tabla tiene
    /// <c>tenant_id</c> UNIQUE pero <c>transit_office_id</c> NO: varios tenants pueden apuntar al mismo
    /// OT. Con un JOIN, un OT con dos perfiles duplicaría filas; con EXISTS, el trámite es elegible si
    /// ALGÚN perfil marca ese OT en modo quipux, y cada trámite aparece a lo sumo una vez.</para>
    /// </summary>
    public async Task<IReadOnlyList<QuipuxTramiteElegible>> ListElegiblesSinSubmissionAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return [];

        if (!db.Database.IsRelational())
        {
            // Tests (InMemory): el predicado depende del jsonb external_refs, que EF no mapea, así
            // que no hay camino LINQ equivalente. La elegibilidad se cubre con pruebas de
            // integración contra Postgres; aquí no hay nada que reclamar.
            return [];
        }

        // Elegibilidad = la SECRETARÍA DESTINO está integrada con Quipux para la familia de ESE
        // trámite, y se conoce su DIVIPO.
        //
        // Ojo con lo que NO se mira aquí: admin.transit_office_profiles.operation_mode = 'quipux'.
        // Ese perfil describe al OT-CLIENTE cuya consola queda en solo lectura porque aprueba dentro
        // de Quipux, y solo existe para los organismos que son tenants de FLIT (12 de 317). La
        // secretaría a la que se radica normalmente NO es cliente —se usa Quipux precisamente
        // porque no usa el dashboard—, así que filtrar por el perfil dejaba fuera a 305 de 317.
        // Comparten el nombre "Quipux" y son cosas distintas.
        //
        // La bandera se elige por la familia declarada en external_refs (MATRICULA/TRASPASO/OTROS),
        // que replica las tres columnas id_parinttrasec_* de FLIT 1.0: el alta es gradual y una
        // secretaría puede estar integrada para traspasos y no para matrículas. No se usa
        // procedure_types.family: esa columna está inconsistente (conviven cuatro valores por seeds
        // solapados, y VEHICULAR ni existe en el enum).
        const string sql = """
            SELECT pi.id, pi.tenant_id
            FROM tramites.procedure_instances pi
            JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
            JOIN catalogs.transit_offices o  ON o.id = pi.transit_office_id
            WHERE pi.status = @status
              AND pi.deleted_at IS NULL
              AND pt.external_refs -> 'quipux' IS NOT NULL
              AND o.is_active
              AND NULLIF(o.divipo_code, '') IS NOT NULL
              AND CASE pt.external_refs -> 'quipux' ->> 'familia'
                    WHEN 'MATRICULA' THEN o.quipux_registration
                    WHEN 'TRASPASO'  THEN o.quipux_transfer
                    WHEN 'OTROS'     THEN o.quipux_other
                    ELSE false
                  END
              AND NOT EXISTS (
                    SELECT 1
                    FROM tramites.quipux_submissions s
                    WHERE s.procedure_instance_id = pi.id
                      AND s.status IN ('pendiente', 'registrado'))
            ORDER BY pi.created_at
            LIMIT @limit
            """;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // Sin transacción propia: es una lectura sin locking. Si el llamador ya tiene una abierta,
            // se engancha a ella (evita "connection already in a transaction").
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = sql;
            AddParam(cmd, "status", TramiteEstado.Preparado);
            AddParam(cmd, "limit", limit);

            var elegibles = new List<QuipuxTramiteElegible>(limit);
            var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    elegibles.Add(new QuipuxTramiteElegible(reader.GetGuid(0), reader.GetGuid(1)));
            }

            return elegibles;
        }
    }

    /// <summary>Marca el intento de radicación: cuenta aunque el proceso muera después.</summary>
    /// <summary>
    /// Sella el lease y consume un intento. <c>claimed_at</c> se estampa AL RECLAMAR, no al
    /// terminar: es lo que mantiene la fila fuera del alcance de otro worker mientras el POST a
    /// Quipux está en vuelo. El intento se cuenta antes de procesar para que un proceso que muera
    /// a media radicación no obtenga reintentos gratis.
    /// </summary>
    private static void MarcarIntento(QuipuxSubmission row)
    {
        var ahora = DateTimeOffset.UtcNow;
        row.Attempts += 1;
        row.ClaimedAt = ahora;
        row.UpdatedAt = ahora;
    }

    /// <summary>Estampa el sondeo: saca la fila de la ventana de frescura y consume presupuesto.</summary>
    private static void MarcarSondeo(QuipuxSubmission row)
    {
        row.LastPolledAt = DateTimeOffset.UtcNow;
        row.PollCount += 1;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Los dos estados sobre los que se define <c>uq_quipux_submissions_activa</c>.</summary>
    private static bool EsActiva(string? status) =>
        status is not null && QuipuxSubmissionEstado.Activos.Contains(status, StringComparer.Ordinal);

    /// <summary>
    /// Ejecuta el <c>SELECT ... FOR UPDATE SKIP LOCKED</c> sobre la conexión/transacción actuales y
    /// devuelve el id reclamado (null si no hay fila reclamable). SQL crudo: EF Core no expone el
    /// locking clause en LINQ.
    /// </summary>
    private static async Task<Guid?> ClaimIdAsync(
        FlitDbContext db, string sql, Action<DbCommand> bindParams, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            bindParams(cmd);

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                return await reader.ReadAsync(ct).ConfigureAwait(false) ? reader.GetGuid(0) : null;
            }
        }
    }

    /// <summary>Parámetro tipado por el provider — nunca concatenación de SQL.</summary>
    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
