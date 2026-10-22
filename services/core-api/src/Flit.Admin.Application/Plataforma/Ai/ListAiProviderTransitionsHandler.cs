using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

/// <summary>HU #13512 — historial paginado de transiciones por tool.</summary>
public sealed class ListAiProviderTransitionsHandler
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IAiToolProviderSettingsRepository _repository;

    public ListAiProviderTransitionsHandler(IAiToolProviderSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AiProviderTransitionsPageView?> HandleAsync(
        string toolCode,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!AiToolCodes.IsKnown(toolCode))
        {
            return null;
        }

        var normalizedPage = page is null or < 1 ? 1 : page.Value;
        var normalizedPageSize = NormalizePageSize(pageSize);
        var normalizedTool = AiToolCodes.Normalize(toolCode);

        var (items, totalCount) = await _repository
            .ListTransitionsAsync(normalizedTool, normalizedPage, normalizedPageSize, cancellationToken)
            .ConfigureAwait(false);

        var views = items
            .Select(t => new AiProviderTransitionView(
                t.Id,
                t.ToolCode,
                t.FromProvider,
                t.ToProvider,
                t.Reason,
                t.Detail,
                t.ActorUserId,
                t.OccurredAt))
            .ToList();

        return new AiProviderTransitionsPageView(views, totalCount, normalizedPage, normalizedPageSize);
    }

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null or < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }
}
