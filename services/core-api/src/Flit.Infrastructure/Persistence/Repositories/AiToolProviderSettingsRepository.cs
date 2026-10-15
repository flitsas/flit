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

        var row = await db.AiToolProviderSettings
            .FirstOrDefaultAsync(x => x.ToolCode == normalizedTool, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return;
        }

        if (!string.Equals(row.ActiveProvider, to, StringComparison.Ordinal))
        {
            row.ActiveProvider = to;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var trimmedDetail = string.IsNullOrWhiteSpace(detail)
            ? null
            : detail.Trim()[..Math.Min(detail.Trim().Length, 1000)];

        var transition = new AiProviderTransitionRow
        {
            Id = Guid.CreateVersion7(),
            ToolCode = normalizedTool,
            FromProvider = from,
            ToProvider = to,
            Reason = "failover_auto",
            Detail = trimmedDetail,
            ActorUserId = null,
            OccurredAt = occurredAt,
        };

        var alert = new AiFailoverAlertRow
        {
            Id = Guid.CreateVersion7(),
            TransitionId = transition.Id,
            ToolCode = normalizedTool,
            FromProvider = from,
            ToProvider = to,
            OccurredAt = occurredAt,
        };

        db.AiProviderTransitions.Add(transition);
        db.AiFailoverAlerts.Add(alert);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
