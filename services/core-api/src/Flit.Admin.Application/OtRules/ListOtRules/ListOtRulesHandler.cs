using Flit.Admin.Domain.OtRules;

namespace Flit.Admin.Application.OtRules.ListOtRules;

public sealed class ListOtRulesQuery
{
    public Guid TenantId { get; init; }
}

public sealed class ListOtRulesResult
{
    public IReadOnlyList<OtRuleResponse> Data { get; init; } = Array.Empty<OtRuleResponse>();
}

/// <summary>Lista reglas OT del tenant (soporte FE / operación).</summary>
public sealed class ListOtRulesHandler
{
    private readonly IOtRuleRepository _repository;

    public ListOtRulesHandler(IOtRuleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListOtRulesResult> HandleAsync(
        ListOtRulesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rules = await _repository.ListByTenantAsync(query.TenantId, cancellationToken).ConfigureAwait(false);
        return new ListOtRulesResult
        {
            Data = rules.Select(OtRuleMapper.ToResponse).ToList(),
        };
    }
}
