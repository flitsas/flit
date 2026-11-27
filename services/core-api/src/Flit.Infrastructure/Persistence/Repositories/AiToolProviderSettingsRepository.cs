using System.Text.Json;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class AiToolProviderSettingsRepository(FlitDbContext db) : IAiToolProviderSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<AiToolProviderSettings>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.AiToolProviderSettings
            .AsNoTracking()
            .OrderBy(x => x.ToolCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<AiToolProviderSettings?> GetByToolCodeAsync(
        string toolCode,
        CancellationToken cancellationToken = default)
    {
        var row = await db.AiToolProviderSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ToolCode == toolCode, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    public async Task<AiToolProviderSettings?> SaveSettingsAsync(
        string toolCode,
        bool autoReturnToPrimary,
        IReadOnlyList<AiProviderSlot> providers,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var row = await db.AiToolProviderSettings
            .FirstOrDefaultAsync(x => x.ToolCode == toolCode, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        row.AutoReturnToPrimary = autoReturnToPrimary;
        row.ProvidersJson = SerializeProviders(providers);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = updatedBy;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(row);
    }

    public async Task<(ActivateProviderResult Result, AiToolProviderSettings? Settings)> ActivateProviderAsync(
        string toolCode,
        string providerCode,
        Guid? actorUserId,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        var row = await db.AiToolProviderSettings
            .FirstOrDefaultAsync(x => x.ToolCode == toolCode, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return (ActivateProviderResult.ToolNotFound, null);
        }

        var slots = ParseProviders(row.ProvidersJson);
        var slot = slots.FirstOrDefault(p => string.Equals(p.ProviderCode, providerCode, StringComparison.Ordinal));
        if (slot is null)
        {
            return (ActivateProviderResult.UnknownProvider, null);
        }

        if (!slot.Enabled)
        {
            return (ActivateProviderResult.ProviderDisabled, null);
        }

        if (!AiProviderCapabilities.SupportsTool(toolCode, providerCode))
        {
            return (ActivateProviderResult.ProviderNotCapable, null);
        }

        if (string.Equals(row.ActiveProvider, providerCode, StringComparison.Ordinal))
        {
            return (ActivateProviderResult.Unchanged, Map(row));
        }

        var fromProvider = row.ActiveProvider;
        row.ActiveProvider = providerCode;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actorUserId;

        var transition = new AiProviderTransitionRow
        {
            Id = Guid.CreateVersion7(),
            ToolCode = toolCode,
            FromProvider = fromProvider,
            ToProvider = providerCode,
            Reason = "manual",
            Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim()[..Math.Min(detail.Trim().Length, 1000)],
            ActorUserId = actorUserId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        db.AiProviderTransitions.Add(transition);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (ActivateProviderResult.ToolNotFound, null);
        }

        return (ActivateProviderResult.Ok, Map(row));
    }

    public async Task RecordFailoverAsync(
        string toolCode,
        string fromProvider,
        string toProvider,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        var normalizedTool = AiToolCodes.Normalize(toolCode);
        var from = AiProviderCodes.Normalize(fromProvider);
        var to = AiProviderCodes.Normalize(toProvider);

        var trimmedDetail = string.IsNullOrWhiteSpace(detail)
            ? null
            : detail.Trim()[..Math.Min(detail.Trim().Length, 1000)];

        // El modelo EF no relaciona la alerta con su transición (las dos tablas se crean por SQL), así que
        // en un solo SaveChanges podía insertar la alerta primero y chocar con fk_ai_failover_alerts_transition:
        // ningún failover automático llegaba a quedar guardado. Se guardan en dos pasos dentro de una
        // transacción; con EnableRetryOnFailure la transacción tiene que ir dentro de la estrategia.
        await EnTransaccionAsync(
                ct => GuardarTransicionAsync(normalizedTool, from, to, "failover_auto", trimmedDetail, conAlerta: true, soloSiActivo: null, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordAutoReturnAsync(
        string toolCode,
        string fromProvider,
        string toProvider,
        CancellationToken cancellationToken = default)
    {
        var normalizedTool = AiToolCodes.Normalize(toolCode);
        var from = AiProviderCodes.Normalize(fromProvider);
        var to = AiProviderCodes.Normalize(toProvider);

        // Solo si el activo sigue siendo el de respaldo: si entretanto alguien lo cambió a mano (u otra
        // réplica ya regresó), no se pisa esa decisión ni se duplica la transición.
        await EnTransaccionAsync(
                ct => GuardarTransicionAsync(normalizedTool, from, to, "auto_return", "el proveedor principal volvió a responder", conAlerta: false, soloSiActivo: from, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnTransaccionAsync(Func<CancellationToken, Task> trabajo, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await trabajo(cancellationToken).ConfigureAwait(false);
            return;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
                async ct =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                    await trabajo(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Cambia el activo y deja la transición (y la alerta, si aplica), la transición primero. Relee la fila
    /// cada vez: si la estrategia reintenta, lo guardado en el intento anterior se deshizo con el rollback.
    /// Con <paramref name="soloSiActivo"/>, no hace nada si el activo ya no es ese proveedor.
    /// </summary>
    private async Task GuardarTransicionAsync(
        string normalizedTool,
        string from,
        string to,
        string reason,
        string? trimmedDetail,
        bool conAlerta,
        string? soloSiActivo,
        CancellationToken cancellationToken)
    {
        // Restos de un intento anterior de la estrategia: ya no existen en la base y no deben reinsertarse.
        foreach (var previa in db.ChangeTracker.Entries()
                     .Where(e => e.Entity is AiProviderTransitionRow or AiFailoverAlertRow)
                     .ToList())
        {
            previa.State = EntityState.Detached;
        }

        var row = await db.AiToolProviderSettings
            .FirstOrDefaultAsync(x => x.ToolCode == normalizedTool, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return;
        }

        await db.Entry(row).ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (soloSiActivo is not null && !string.Equals(row.ActiveProvider, soloSiActivo, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(row.ActiveProvider, to, StringComparison.Ordinal))
        {
            row.ActiveProvider = to;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var transition = new AiProviderTransitionRow
        {
            Id = Guid.CreateVersion7(),
            ToolCode = normalizedTool,
            FromProvider = from,
            ToProvider = to,
            Reason = reason,
            Detail = trimmedDetail,
            ActorUserId = null,
            OccurredAt = occurredAt,
        };

        db.AiProviderTransitions.Add(transition);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!conAlerta)
        {
            return;
        }

        db.AiFailoverAlerts.Add(new AiFailoverAlertRow
        {
            Id = Guid.CreateVersion7(),
            TransitionId = transition.Id,
            ToolCode = normalizedTool,
            FromProvider = from,
            ToProvider = to,
            OccurredAt = occurredAt,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<AiProviderTransition> Items, long TotalCount)> ListTransitionsAsync(
        string toolCode,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedTool = AiToolCodes.Normalize(toolCode);
        var query = db.AiProviderTransitions
            .AsNoTracking()
            .Where(x => x.ToolCode == normalizedTool);

        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (rows.Select(MapTransition).ToList(), totalCount);
    }

    public async Task<IReadOnlyList<AiFailoverAlert>> ListAlertsAsync(
        bool? acked,
        CancellationToken cancellationToken = default)
    {
        var query = db.AiFailoverAlerts.AsNoTracking();

        if (acked is true)
        {
            query = query.Where(x => x.AckedAt != null);
        }
        else if (acked is false)
        {
            query = query.Where(x => x.AckedAt == null);
        }

        var rows = await query
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(MapAlert).ToList();
    }

    public async Task<AiFailoverAlert?> AcknowledgeAlertAsync(
        Guid alertId,
        Guid? ackedBy,
        CancellationToken cancellationToken = default)
    {
        var row = await db.AiFailoverAlerts
            .FirstOrDefaultAsync(x => x.Id == alertId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        if (row.AckedAt is null)
        {
            row.AckedAt = DateTimeOffset.UtcNow;
            row.AckedBy = ackedBy;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return MapAlert(row);
    }

    private static AiToolProviderSettings Map(AiToolProviderSettingsRow row) =>
        new(
            row.Id,
            row.ToolCode,
            row.ActiveProvider,
            row.AutoReturnToPrimary,
            ParseProviders(row.ProvidersJson),
            row.RowVersion,
            row.CreatedAt,
            row.UpdatedAt,
            row.UpdatedBy);

    private static AiProviderTransition MapTransition(AiProviderTransitionRow row) =>
        new(
            row.Id,
            row.ToolCode,
            row.FromProvider,
            row.ToProvider,
            row.Reason,
            row.Detail,
            row.ActorUserId,
            row.OccurredAt);

    private static AiFailoverAlert MapAlert(AiFailoverAlertRow row) =>
        new(
            row.Id,
            row.TransitionId,
            row.ToolCode,
            row.FromProvider,
            row.ToProvider,
            row.OccurredAt,
            row.AckedAt,
            row.AckedBy);

    private static IReadOnlyList<AiProviderSlot> ParseProviders(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<AiProviderSlot>();
        }

        var dtos = JsonSerializer.Deserialize<List<AiProviderSlotJson>>(json, JsonOptions);
        if (dtos is null || dtos.Count == 0)
        {
            return Array.Empty<AiProviderSlot>();
        }

        return dtos
            .Select(d => new AiProviderSlot(
                AiProviderCodes.Normalize(d.ProviderCode),
                d.Enabled,
                d.PriorityRank))
            .ToList();
    }

    private static string SerializeProviders(IReadOnlyList<AiProviderSlot> providers)
    {
        var payload = providers
            .OrderBy(p => p.PriorityRank)
            .Select(p => new AiProviderSlotJson
            {
                ProviderCode = p.ProviderCode,
                Enabled = p.Enabled,
                PriorityRank = p.PriorityRank,
            })
            .ToList();

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed class AiProviderSlotJson
    {
        public string ProviderCode { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public int PriorityRank { get; set; }
    }
}
