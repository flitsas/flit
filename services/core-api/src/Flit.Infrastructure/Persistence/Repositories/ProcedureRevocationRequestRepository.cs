using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #12571 (Feature #12565) — persistencia de <see cref="ProcedureRevocationRequest"/>. Aislamiento por
/// tenant en el <c>WHERE</c> (mismo patrón que <see cref="ProcedureInstanceRepository"/>); la RLS de la
/// tabla es defensa en profundidad.
/// </summary>
internal sealed class ProcedureRevocationRequestRepository(FlitDbContext db) : IProcedureRevocationRequestRepository
{
    private const string ActiveUniqueIndex = "uq_procedure_revocation_requests_active_per_instance";

    public Task<ProcedureRevocationRequest?> FindActiveAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default) =>
        db.ProcedureRevocationRequests
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == procedureInstanceId)
            .Where(x => x.Status == ProcedureRevocationRequestStatus.Solicitada
                || x.Status == ProcedureRevocationRequestStatus.EnRevision)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ProcedureRevocationRequest?> FindLastAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default) =>
        db.ProcedureRevocationRequests
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == procedureInstanceId)
            .OrderByDescending(x => x.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetNextAttemptNumberAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default)
    {
        var max = await db.ProcedureRevocationRequests
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == procedureInstanceId)
            .Select(x => (int?)x.AttemptNumber)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        return (max ?? 0) + 1;
    }

    public void Add(ProcedureRevocationRequest request) => db.ProcedureRevocationRequests.Add(request);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsActiveRequestUniqueViolation(ex))
        {
            // AC4 — dos solicitudes concurrentes para el mismo trámite: el gate en memoria
            // (RevocationRequestGate) ya cubrió el caso normal, esto cierra la carrera. Se traduce el
            // 23505 a excepción de dominio (checklist §B12); el endpoint (HU #12572) responde 409.
            throw new ActiveRevocationRequestExistsException();
        }
    }

    private static bool IsActiveRequestUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && string.Equals(pg.ConstraintName, ActiveUniqueIndex, StringComparison.Ordinal);

    /// <summary>HU #12572 — ver XML doc de la interfaz.</summary>
    public Task<DateTimeOffset?> GetFirstApprovedAtAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default) =>
        db.ProcedureInstanceStatusHistories
            .Where(h => h.TenantId == tenantId
                && h.ProcedureInstanceId == procedureInstanceId
                && h.ToStatus == TramiteEstado.Aprobado)
            .OrderBy(h => h.ChangedAt)
            .Select(h => (DateTimeOffset?)h.ChangedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// HU #12572 — ver XML doc de la interfaz. Lectura CROSS-TENANT (sin filtrar por
    /// <c>tenant_id</c>): mismo criterio que <c>OtProfileRepository.GetByTransitOfficeAsync</c>, el
    /// perfil pertenece al organismo, no al tenant cliente que lo consulta.
    /// </summary>
    public Task<int?> GetRevocationWindowBusinessDaysAsync(
        Guid transitOfficeId, CancellationToken cancellationToken = default) =>
        db.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TransitOfficeId == transitOfficeId)
            .Select(p => p.RevocationWindowBusinessDays)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>HU #12578 — ver XML doc de la interfaz. Alcance normal (RLS del tenant intacta).</summary>
    public Task<RevocationRequestListPage> ListForTenantAsync(
        Guid tenantId, RevocationRequestListFilter filter, CancellationToken cancellationToken = default) =>
        ListCoreAsync(tenantId, scopeTransitOfficeId: null, filter, cancellationToken);

    /// <summary>
    /// HU #12578 — ver XML doc de la interfaz. CROSS-TENANT: mismo <c>SET LOCAL row_security = off</c>
    /// dentro de una transacción que <c>OtClientProcedureRepository.ExecuteCrossTenantReadAsync</c> (no
    /// se reimplementa esa lógica de grants; aquí solo hace falta bypasear la RLS de ESTA tabla, el
    /// organismo ya viene resuelto por el caller — ver <c>IOtClientProcedureRepository.ResolveTransitOfficeIdAsync</c>).
    /// </summary>
    public Task<RevocationRequestListPage> ListForTransitOfficeAsync(
        Guid transitOfficeId, RevocationRequestListFilter filter, CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            () => ListCoreAsync(tenantId: null, transitOfficeId, filter, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Consulta compartida de <see cref="ListForTenantAsync"/>/<see cref="ListForTransitOfficeAsync"/>:
    /// <c>procedure_revocation_requests</c> LEFT JOIN <c>procedure_instances</c> (radicado/placa/OT) LEFT
    /// JOIN <c>transit_offices</c> (nombre del organismo) — un solo roundtrip, sin N+1. El único WHERE
    /// obligatorio de aislamiento es <paramref name="tenantId"/> XOR <paramref name="scopeTransitOfficeId"/>
    /// (nunca ambos: uno u otro trae la llamada pública).
    /// </summary>
    private async Task<RevocationRequestListPage> ListCoreAsync(
        Guid? tenantId,
        Guid? scopeTransitOfficeId,
        RevocationRequestListFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query =
            from r in db.ProcedureRevocationRequests.AsNoTracking()
            join p in db.ProcedureInstances.AsNoTracking() on r.ProcedureInstanceId equals p.Id
            join t in db.TransitOffices.AsNoTracking() on p.TransitOfficeId equals (Guid?)t.Id into offices
            from office in offices.DefaultIfEmpty()
            select new { r, p, office };

        if (tenantId is { } tid)
        {
            query = query.Where(x => x.r.TenantId == tid);
        }

        // Alcance OT: el organismo YA viene resuelto por el caller (cross-tenant por diseño).
        if (scopeTransitOfficeId is { } scopeOffice)
        {
            query = query.Where(x => x.p.TransitOfficeId == scopeOffice);
        }

        // Filtro opcional por organismo (solo tiene sentido del lado gestor — ver XML doc del filtro).
        if (filter.TransitOfficeId is { } filterOffice)
        {
            query = query.Where(x => x.p.TransitOfficeId == filterOffice);
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            query = query.Where(x => statuses.Contains(x.r.Status));
        }

        if (filter.RequestedFrom is { } from)
        {
            query = query.Where(x => x.r.RequestedAt >= from);
        }

        if (filter.RequestedTo is { } to)
        {
            query = query.Where(x => x.r.RequestedAt <= to);
        }

        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (totalCount == 0)
        {
            return RevocationRequestListPage.Empty;
        }

        var items = await query
            .OrderByDescending(x => x.r.RequestedAt)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(x => new RevocationRequestListItem(
                x.r.Id,
                x.r.ProcedureInstanceId,
                x.p.ReferenceNumber,
                x.p.Plate,
                x.p.TransitOfficeId,
                x.office != null ? x.office.Name : null,
                x.r.Status,
                x.r.AttemptNumber,
                x.r.RequestedAt,
                x.r.DecidedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RevocationRequestListPage(items, totalCount);
    }

    /// <summary>
    /// Bypasea la RLS de <c>tramites.procedure_revocation_requests</c> (aísla por <c>tenant_id</c> del
    /// CLIENTE) para la lectura CROSS-TENANT del lado OT — MISMO patrón que
    /// <c>OtClientProcedureRepository.ExecuteCrossTenantReadAsync</c> (no se referencia esa clase para no
    /// acoplar los repositorios de dos módulos distintos por una utilidad de 15 líneas). En proveedor
    /// InMemory (tests) delega directo, sin transacción ni <c>SET LOCAL</c>.
    /// </summary>
    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await db.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);

                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
