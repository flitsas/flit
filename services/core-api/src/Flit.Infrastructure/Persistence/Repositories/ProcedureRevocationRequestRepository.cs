using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Repositories;
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
}
