using Flit.Modules.Quipux.Domain.Consola;
using Flit.Modules.Quipux.Domain.Envios;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura y operación de la consola de cola Quipux del SuperAdmin (HU #10774). Distinto del
/// <see cref="QuipuxSubmissionRepository"/> (el de los workers, con claim <c>FOR UPDATE SKIP
/// LOCKED</c>): aquí se lista por secretaría destino y se aplican dos transiciones manuales acotadas.
///
/// <para><b>Acceso cross-tenant.</b> <c>tramites.quipux_submissions</c> y <c>procedure_instances</c>
/// llevan RLS <c>tenant_isolation</c>, pero el rol de conexión de core-api es el PROPIETARIO de las
/// tablas y éstas no declaran <c>FORCE ROW LEVEL SECURITY</c>, así que la política no se le aplica y
/// una consulta normal ve todos los tenants. Es exactamente el enfoque de
/// <see cref="QuipuxSubmissionRepository"/>: NO se fija el GUC ni se toca <c>row_security</c>; el
/// aislamiento se sostiene con el filtro EXPLÍCITO por <c>transit_office_id</c> de la secretaría cuya
/// página se está viendo.</para>
///
/// <para><b>Guard de concurrencia.</b> Las transiciones no son un read-modify-write a ciegas: usan
/// <c>ExecuteUpdate</c> con <c>WHERE ... AND status = &lt;esperado&gt;</c>, de modo que si un worker
/// reclamó y avanzó la fila entre la carga de la pantalla y el clic, el UPDATE afecta 0 filas y se
/// reporta <see cref="QuipuxManualOpStatus.InvalidState"/> en vez de pisar el trabajo del worker.
/// La rama <c>InMemory</c> (tests) no soporta <c>ExecuteUpdate</c> ni concurrencia real, así que ahí
/// se hace el equivalente rastreado.</para>
/// </summary>
internal sealed class DbQuipuxSubmissionConsoleRepository(FlitDbContext db) : IQuipuxSubmissionConsoleRepository
{
    private const string CancelReason = "Cancelado manualmente desde la consola de cola.";

    public async Task<QuipuxSubmissionQueuePage> ListByTransitOfficeAsync(
        Guid transitOfficeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // JOIN submission ↔ trámite, filtrado por la secretaría destino. Todos los estados (activa +
        // histórico), del más nuevo al más viejo.
        var scoped =
            from s in db.QuipuxSubmissions.AsNoTracking()
            join pi in db.ProcedureInstances.AsNoTracking() on s.ProcedureInstanceId equals pi.Id
            where pi.TransitOfficeId == transitOfficeId
            select new { s, pi };

        var total = await scoped.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return new QuipuxSubmissionQueuePage([], 0);
        }

        var items = await (
                from x in scoped
                join pt in db.ProcedureTypes.AsNoTracking() on x.pi.ProcedureTypeId equals pt.Id
                join t in db.Tenants.AsNoTracking() on x.pi.TenantId equals t.Id
                orderby x.s.CreatedAt descending
                select new QuipuxSubmissionQueueItem
                {
                    Id = x.s.Id,
                    ProcedureInstanceId = x.s.ProcedureInstanceId,
                    ReferenceNumber = x.pi.ReferenceNumber,
                    ProcedureTypeName = pt.Name,
                    ClientTenantName = t.LegalName,
                    DocumentName = x.s.DocumentName,
                    Status = x.s.Status,
                    Attempts = x.s.Attempts,
                    PollCount = x.s.PollCount,
                    QxRegisterCode = x.s.QxRegisterCode,
                    QxProcedureCode = x.s.QxProcedureCode,
                    RejectionReason = x.s.RejectionReason,
                    CreatedAt = x.s.CreatedAt,
                    RegisteredAt = x.s.RegisteredAt,
                    LastPolledAt = x.s.LastPolledAt,
                    CompletedAt = x.s.CompletedAt,
                    UpdatedAt = x.s.UpdatedAt,
                })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new QuipuxSubmissionQueuePage(items, total);
    }

    public async Task<QuipuxManualOpResult> RetryAsync(
        Guid submissionId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var row = await LoadScopedAsync(submissionId, transitOfficeId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new QuipuxManualOpResult(QuipuxManualOpStatus.NotFound);
        }

        if (!string.Equals(row.Status, QuipuxSubmissionEstado.Fallido, StringComparison.Ordinal))
        {
            return new QuipuxManualOpResult(QuipuxManualOpStatus.InvalidState, row.TenantId, row.Status);
        }

        // uq_quipux_submissions_activa: a lo sumo una radicación viva por trámite. Si ya reapareció
        // otra (pendiente/registrado), re-encolar ésta la violaría: se reporta Conflict sin tocar BD.
        var yaHayActiva = await db.QuipuxSubmissions
            .AsNoTracking()
            .AnyAsync(
                s => s.ProcedureInstanceId == row.ProcedureInstanceId
                    && s.Id != row.Id
                    && (s.Status == QuipuxSubmissionEstado.Pendiente
                        || s.Status == QuipuxSubmissionEstado.Registrado),
                cancellationToken)
            .ConfigureAwait(false);

        if (yaHayActiva)
        {
            return new QuipuxManualOpResult(QuipuxManualOpStatus.Conflict, row.TenantId, row.Status);
        }

        var previousStatus = row.Status;
        var previousAttempts = row.Attempts;
        var now = DateTimeOffset.UtcNow;

        try
        {
            var afectadas = await ExecuteTransitionAsync(
                submissionId,
                expectedStatus: QuipuxSubmissionEstado.Fallido,
                apply: r =>
                {
                    r.Status = QuipuxSubmissionEstado.Pendiente;
                    // Presupuesto y lease frescos: el registrador vuelve a reclamarla desde cero.
                    r.Attempts = 0;
                    r.ClaimedAt = null;
                    r.CompletedAt = null;
                    r.QxRegisterCode = null;
                    // El motivo del fallo previo no debe verse en una fila que vuelve a estar en cola.
                    r.RejectionReason = null;
                    r.UpdatedAt = now;
                },
                guarded => guarded.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(r => r.Status, QuipuxSubmissionEstado.Pendiente)
                        .SetProperty(r => r.Attempts, 0)
                        .SetProperty(r => r.ClaimedAt, (DateTimeOffset?)null)
                        .SetProperty(r => r.CompletedAt, (DateTimeOffset?)null)
                        .SetProperty(r => r.QxRegisterCode, (int?)null)
                        .SetProperty(r => r.RejectionReason, (string?)null)
                        .SetProperty(r => r.UpdatedAt, now),
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (afectadas == 0)
            {
                // Cambió de estado entre la carga y el UPDATE (otro operador o el propio worker).
                return new QuipuxManualOpResult(QuipuxManualOpStatus.InvalidState, row.TenantId, previousStatus);
            }
        }
        catch (DbUpdateException)
        {
            // Carrera con uq_quipux_submissions_activa: otra radicación viva apareció entre el chequeo
            // y el UPDATE. La BD es la red de seguridad; se traduce a Conflict.
            return new QuipuxManualOpResult(QuipuxManualOpStatus.Conflict, row.TenantId, previousStatus);
        }

        return new QuipuxManualOpResult(QuipuxManualOpStatus.Ok, row.TenantId, previousStatus, previousAttempts);
    }

    public async Task<QuipuxManualOpResult> CancelAsync(
        Guid submissionId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var row = await LoadScopedAsync(submissionId, transitOfficeId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new QuipuxManualOpResult(QuipuxManualOpStatus.NotFound);
        }

        if (!string.Equals(row.Status, QuipuxSubmissionEstado.Pendiente, StringComparison.Ordinal))
        {
            return new QuipuxManualOpResult(QuipuxManualOpStatus.InvalidState, row.TenantId, row.Status);
        }

        var previousStatus = row.Status;
        var now = DateTimeOffset.UtcNow;

        var afectadas = await ExecuteTransitionAsync(
            submissionId,
            expectedStatus: QuipuxSubmissionEstado.Pendiente,
            apply: r =>
            {
                r.Status = QuipuxSubmissionEstado.Fallido;
                r.CompletedAt = now;
                r.RejectionReason = CancelReason;
                r.UpdatedAt = now;
            },
            guarded => guarded.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.Status, QuipuxSubmissionEstado.Fallido)
                    .SetProperty(r => r.CompletedAt, (DateTimeOffset?)now)
                    .SetProperty(r => r.RejectionReason, (string?)CancelReason)
                    .SetProperty(r => r.UpdatedAt, (DateTimeOffset?)now),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (afectadas == 0)
        {
            // Un worker la reclamó y la registró entre la carga y el clic: no se pisa su trabajo.
            return new QuipuxManualOpResult(QuipuxManualOpStatus.InvalidState, row.TenantId, previousStatus);
        }

        return new QuipuxManualOpResult(QuipuxManualOpStatus.Ok, row.TenantId, previousStatus);
    }

    /// <summary>
    /// Carga la submission SOLO si pertenece a la secretaría destino indicada (join a
    /// <c>procedure_instances</c>). Devuelve <c>null</c> si no existe o es de otro OT — en cuyo caso
    /// el caso de uso responde NotFound y no se filtra la existencia de una submission ajena.
    /// </summary>
    private async Task<QuipuxSubmission?> LoadScopedAsync(
        Guid submissionId,
        Guid transitOfficeId,
        CancellationToken cancellationToken) =>
        await (
            from s in db.QuipuxSubmissions.AsNoTracking()
            join pi in db.ProcedureInstances.AsNoTracking() on s.ProcedureInstanceId equals pi.Id
            where s.Id == submissionId && pi.TransitOfficeId == transitOfficeId
            select s)
        .FirstOrDefaultAsync(cancellationToken)
        .ConfigureAwait(false);

    /// <summary>
    /// Aplica una transición de estado guardada por <paramref name="expectedStatus"/> y devuelve el
    /// número de filas afectadas (0 = la fila ya no estaba en el estado esperado). En relacional usa
    /// <c>ExecuteUpdate</c> (una sola sentencia atómica con el guard en el <c>WHERE</c>); en InMemory
    /// —sin soporte de <c>ExecuteUpdate</c>— hace el equivalente rastreado.
    /// </summary>
    private async Task<int> ExecuteTransitionAsync(
        Guid submissionId,
        string expectedStatus,
        Action<QuipuxSubmission> apply,
        Func<IQueryable<QuipuxSubmission>, Task<int>> relationalUpdate,
        CancellationToken cancellationToken)
    {
        // El guard vive en el WHERE (id + estado esperado): en relacional es una sola sentencia
        // atómica; el llamador pasa el ExecuteUpdate con sus setters (así el tipo del lambda se
        // infiere y no hay que nombrar SetPropertyCalls).
        var guarded = db.QuipuxSubmissions.Where(s => s.Id == submissionId && s.Status == expectedStatus);

        if (db.Database.IsRelational())
        {
            return await relationalUpdate(guarded).ConfigureAwait(false);
        }

        var tracked = await guarded
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tracked is null)
        {
            return 0;
        }

        apply(tracked);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return 1;
    }
}
