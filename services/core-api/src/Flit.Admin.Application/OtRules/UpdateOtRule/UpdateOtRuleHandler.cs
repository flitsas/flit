using Flit.Admin.Domain.OtRules;

namespace Flit.Admin.Application.OtRules.UpdateOtRule;

public sealed class UpdateOtRuleCommand
{
    public Guid TenantId { get; init; }

    public Guid RuleId { get; init; }

    public Guid? ChangedBy { get; init; }

    public UpdateOtRuleRequest Request { get; init; } = new();
}

public enum UpdateOtRuleStatus
{
    Updated,
    NotFound,
    ValidationFailed,
}

public sealed record FieldError(string Field, string Message);

public sealed class UpdateOtRuleResult
{
    public UpdateOtRuleStatus Status { get; init; }

    public OtRuleResponse? Rule { get; init; }

    public IReadOnlyList<FieldError> Errors { get; init; } = Array.Empty<FieldError>();

    public static UpdateOtRuleResult Updated(OtRuleResponse rule) =>
        new() { Status = UpdateOtRuleStatus.Updated, Rule = rule };

    public static UpdateOtRuleResult NotFound() =>
        new() { Status = UpdateOtRuleStatus.NotFound };

    public static UpdateOtRuleResult ValidationFailed(params FieldError[] errors) =>
        new() { Status = UpdateOtRuleStatus.ValidationFailed, Errors = errors };
}

/// <summary>Hot-swap de reglas OT — activar/desactivar sin restart (HU #10221 AC4).</summary>
public sealed class UpdateOtRuleHandler
{
    private readonly IOtRuleRepository _repository;

    public UpdateOtRuleHandler(IOtRuleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateOtRuleResult> HandleAsync(
        UpdateOtRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        if (command.Request.IsEnabled is not bool isEnabled)
        {
            return UpdateOtRuleResult.ValidationFailed(new FieldError("is_enabled", "IS_ENABLED_REQUIRED"));
        }

        var updated = await _repository.UpdateEnabledAsync(
            command.TenantId,
            command.RuleId,
            isEnabled,
            command.ChangedBy,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? UpdateOtRuleResult.NotFound()
            : UpdateOtRuleResult.Updated(OtRuleMapper.ToResponse(updated));
    }
}
