using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure.RuntConfirmation;

/// <summary>
/// Almacén de la Confirmación RUNT sobre <see cref="FlitDbContext"/>. SCOPE PROPIO POR OPERACIÓN
/// (<see cref="IServiceScopeFactory"/>, el patrón de <c>QuipuxJobRunLog</c>): la corrida graba desde
/// varias tareas a la vez y un DbContext no admite dos operaciones simultáneas. Cada método abre su
/// contexto, guarda y cierra; ninguna transacción cruza operaciones.
///
/// CROSS-TENANT: la corrida barre todos los tenants. Funciona porque el rol de la aplicación es dueño
/// de las tablas y no hay <c>FORCE ROW LEVEL SECURITY</c>, así que las policies no filtran — el mismo
/// supuesto sobre el que corren los workers de Quipux e identidad.
/// </summary>
internal sealed class RuntConfirmationStore(IServiceScopeFactory scopeFactory) : IRuntConfirmationStore
{
    private static readonly string[] FlagsFuera = [RuntConfirmationFlags.NoVerificable, RuntConfirmationFlags.Tope];
    private static readonly string[] ProviderKeysVehiculo = [RuntConfirmationProviderKeys.Kyverum, RuntConfirmationProviderKeys.Verifik];

    public async Task<IReadOnlyList<RuntConfirmationCandidate>> ListUniverseAsync(RuntConfirmationUniverseFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        var graceLimit = filter.NowUtc.AddDays(-filter.GraceDays);
        var excluded = RuntProcedureEquivalence.ExcludedProcedureTypeCodes.ToArray();

        // Universo (HU #12309 AC2): aprobados sin confirmar, bajo el tope, sin marca terminal, tipo en
        // alcance y con la gracia cumplida desde la aprobación (fecha del historial; si falta, updated_at).
        var rows = await db.ProcedureInstances.AsNoTracking()
            .Where(i => i.DeletedAt == null
                && i.Status == TramiteEstado.Aprobado
                && i.RuntConfirmedAt == null
                && i.RuntAttempts < filter.MaxAttempts
                && (i.RuntFlag == null || !FlagsFuera.Contains(i.RuntFlag))
                && !excluded.Contains(i.ProcedureType!.Code))
            .Select(i => new
            {
                i.Id,
                ApprovedAt = db.ProcedureInstanceStatusHistories
                    .Where(h => h.ProcedureInstanceId == i.Id && h.ToStatus == TramiteEstado.Aprobado)
                    .Max(h => (DateTimeOffset?)h.ChangedAt) ?? i.UpdatedAt ?? i.CreatedAt,
            })
            .Where(x => x.ApprovedAt <= graceLimit)
            .OrderBy(x => x.ApprovedAt)
            .Take(filter.Take)
            .Select(x => x.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new List<RuntConfirmationCandidate>(rows.Count);
        foreach (var id in rows)
        {
            var c = await LoadCandidateAsync(db, id, ct).ConfigureAwait(false);
            if (c is not null)
                result.Add(c);
        }

        return result;
    }

    public async Task<RuntConfirmationCandidate?> GetCandidateAsync(Guid instanceId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        return await LoadCandidateAsync(db, instanceId, ct).ConfigureAwait(false);
    }

    private static async Task<RuntConfirmationCandidate?> LoadCandidateAsync(FlitDbContext db, Guid id, CancellationToken ct)
    {
        var i = await db.ProcedureInstances.AsNoTracking()
            .Include(x => x.ProcedureType)
            .Include(x => x.Actors)
            .Include(x => x.FieldValues)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (i is null || i.ProcedureType is null)
            return null;

        var approvedAt = await db.ProcedureInstanceStatusHistories.AsNoTracking()
            .Where(h => h.ProcedureInstanceId == id && h.ToStatus == TramiteEstado.Aprobado)
            .MaxAsync(h => (DateTimeOffset?)h.ChangedAt, ct)
            .ConfigureAwait(false);

        string? otName = null;
        if (i.TransitOfficeId is Guid otId)
        {
            otName = await db.TransitOffices.AsNoTracking()
                .Where(o => o.Id == otId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        string? Field(string key) => i.FieldValues
            .Where(f => f.FieldKey == key && !string.IsNullOrWhiteSpace(f.ValueText))
            .Select(f => f.ValueText!.Trim())
            .FirstOrDefault();

        RuntDocument? Actor(string type)
        {
            var a = i.Actors.Where(x => x.ActorType == type).OrderBy(x => x.Ordinal).FirstOrDefault();
            return a is null || string.IsNullOrWhiteSpace(a.DocumentNumber) ? null : new RuntDocument(a.DocumentType, a.DocumentNumber.Trim());
        }

        var ownerType = Field("owner_document_type");
        var ownerNumber = Field("owner_document_number");
        var owner = ownerNumber is null ? Actor("propietario") : new RuntDocument(ownerType ?? "CC", ownerNumber);

        return new RuntConfirmationCandidate(
            i.Id,
            i.TenantId,
            i.ReferenceNumber,
            i.ProcedureType.Code,
            ProcedureFamilyCodes.FromCodeOrOtros(i.ProcedureType.Family),
            Vin: i.Vin ?? Field("vin"),
            Plate: i.Plate ?? Field("plate"),
            SubmittedAt: i.SubmittedAt,
            ApprovedAt: approvedAt,
            CreatedAt: i.CreatedAt,
            TransitOfficeName: otName,
            Owner: owner,
            Seller: Actor("vendedor"),
            Buyer: Actor("comprador"),
            RuntAttempts: i.RuntAttempts,
            RuntConfirmedAt: i.RuntConfirmedAt,
            RuntFlag: i.RuntFlag);
    }

    public async Task<Guid?> SaveRawPayloadAsync(Guid tenantId, Guid instanceId, string providerKey, string? subjectKey, string rawJson, DateTimeOffset queriedAt, CancellationToken ct = default)
    {
        // Mismo carril y misma sanitización que las certificaciones (HU #11304): sin secretos ni binarios.
        var clean = RawPayloadSanitizer.Sanitize(rawJson);
        if (clean is null)
            return null;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        var entity = new ExternalQueryPayload
        {
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            ProviderKey = providerKey.Length > 40 ? providerKey[..40] : providerKey,
            SubjectKind = Flit.Tramites.Domain.Certifications.RawProviderPayload.VehicleSubject,
            SubjectKey = subjectKey is { Length: > 40 } ? subjectKey[..40] : subjectKey,
            Payload = clean,
            QueriedAt = queriedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ExternalQueryPayloads.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<string?> GetPayloadJsonAsync(Guid payloadId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        return await db.ExternalQueryPayloads.AsNoTracking()
            .Where(p => p.Id == payloadId)
            .Select(p => p.Payload)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetBaselinePayloadJsonAsync(Guid instanceId, DateTimeOffset cutoffAt, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        // El snapshot de radicación es la respuesta del vehículo MÁS RECIENTE anterior al envío al OT:
        // la del wizard. Las de la propia corrida son siempre posteriores, así que no se confunden.
        return await db.ExternalQueryPayloads.AsNoTracking()
            .Where(p => p.ProcedureInstanceId == instanceId
                && p.SubjectKind == Flit.Tramites.Domain.Certifications.RawProviderPayload.VehicleSubject
                && ProviderKeysVehiculo.Contains(p.ProviderKey)
                && p.QueriedAt <= cutoffAt)
            .OrderByDescending(p => p.QueriedAt)
            .Select(p => p.Payload)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<RuntConfirmationRun> StartRunAsync(RuntConfirmationRun run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        db.RuntConfirmationRuns.Add(run);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        db.Entry(run).State = EntityState.Detached;
        return run;
    }

    public async Task FinishRunAsync(RuntConfirmationRun run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = await db.RuntConfirmationRuns.FirstOrDefaultAsync(r => r.Id == run.Id, ct).ConfigureAwait(false);
        if (row is null)
            return;

        row.FinishedAt = run.FinishedAt;
        row.Consulted = run.Consulted;
        row.Confirmed = run.Confirmed;
        row.Pending = run.Pending;
        row.Discrepancies = run.Discrepancies;
        row.Unverifiable = run.Unverifiable;
        row.Errors = run.Errors;
        row.ProviderCalls = run.ProviderCalls;
        row.ErrorMessage = run.ErrorMessage;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> IsRunInProgressAsync(TimeSpan staleAfter, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var since = DateTimeOffset.UtcNow - staleAfter;
        return await db.RuntConfirmationRuns.AsNoTracking()
            .AnyAsync(r => r.FinishedAt == null && r.StartedAt >= since, ct)
            .ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetLastScheduledRunStartedAtAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        return await db.RuntConfirmationRuns.AsNoTracking()
            .Where(r => r.Trigger == RuntConfirmationRunTriggers.Scheduled)
            .MaxAsync(r => (DateTimeOffset?)r.StartedAt, ct)
            .ConfigureAwait(false);
    }

    public async Task RecordAttemptAsync(RuntConfirmationAttempt attempt, RuntConfirmationInstanceUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(update);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        db.RuntConfirmationAttempts.Add(attempt);

        // Solo las columnas runt_*: el status y sus eventos no se tocan (AC8). Se escribe con una
        // sentencia acotada para no arrastrar el row_version de una instancia que otro usuario esté
        // editando en ese instante.
        var instance = await db.ProcedureInstances.FirstOrDefaultAsync(i => i.Id == attempt.ProcedureInstanceId, ct).ConfigureAwait(false);
        if (instance is not null)
        {
            if (update.IncrementAttempts)
                instance.RuntAttempts += 1;
            if (update.ConfirmedAt is not null)
                instance.RuntConfirmedAt = update.ConfirmedAt;
            if (update.ClearFlag)
                instance.RuntFlag = null;
            else if (update.Flag is not null)
                instance.RuntFlag = update.Flag;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<RuntConfirmationAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        return await db.RuntConfirmationAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId, ct).ConfigureAwait(false);
    }
}
