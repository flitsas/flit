using System.Text.Json;
using Flit.Admin.Domain.OtRules;

namespace Flit.Admin.Application.OtRules.CreateOtRule;

public sealed class CreateOtRuleCommand
{
    public Guid TenantId { get; init; }

    public Guid? CreatedBy { get; init; }

    public CreateOtRuleRequest Request { get; init; } = new();
}

public enum CreateOtRuleStatus
{
    Created,
    ValidationFailed,
}

public sealed record FieldError(string Field, string Message);

public sealed class CreateOtRuleResult
{
    public CreateOtRuleStatus Status { get; init; }

    public OtRuleResponse? Rule { get; init; }

    public IReadOnlyList<FieldError> Errors { get; init; } = Array.Empty<FieldError>();

    public static CreateOtRuleResult Created(OtRuleResponse rule) =>
        new() { Status = CreateOtRuleStatus.Created, Rule = rule };

    public static CreateOtRuleResult ValidationFailed(params FieldError[] errors) =>
        new() { Status = CreateOtRuleStatus.ValidationFailed, Errors = errors };
}

/// <summary>Crea una regla OT con condiciones AND/OR (HU #10221 AC1, AC5).</summary>
public sealed class CreateOtRuleHandler
{
    private readonly IOtRuleRepository _repository;

    public CreateOtRuleHandler(IOtRuleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateOtRuleResult> HandleAsync(
        CreateOtRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var errors = Validate(command.Request);
        if (errors.Count > 0)
        {
            return CreateOtRuleResult.ValidationFailed(errors.ToArray());
        }

        var conditions = command.Request.Conditions.Select(c => new OtRuleCondition
        {
            Field = c.Field.Trim(),
            Op = c.Op.Trim(),
            ValueJson = c.Value.GetRawText(),
        }).ToList();

        var action = new OtRuleAction
        {
            Type = command.Request.Action.Type.Trim(),
            QueueName = string.IsNullOrWhiteSpace(command.Request.Action.QueueName)
                ? null
                : command.Request.Action.QueueName.Trim(),
        };

        var created = await _repository.CreateAsync(
            command.TenantId,
            command.Request.Name.Trim(),
            conditions,
            command.Request.Logic.Trim().ToUpperInvariant(),
            action,
            command.CreatedBy,
            cancellationToken).ConfigureAwait(false);

        return CreateOtRuleResult.Created(OtRuleMapper.ToResponse(created));
    }

    private static List<FieldError> Validate(CreateOtRuleRequest request)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", "NAME_REQUIRED"));
        }

        if (request.Conditions.Count == 0)
        {
            errors.Add(new FieldError("conditions", "CONDITIONS_REQUIRED"));
        }
        else
        {
            for (var i = 0; i < request.Conditions.Count; i++)
            {
                var condition = request.Conditions[i];
                if (string.IsNullOrWhiteSpace(condition.Field))
                {
                    errors.Add(new FieldError($"conditions[{i}].field", "FIELD_REQUIRED"));
                }

                if (!OtRuleConstants.SupportedOperators.Contains(condition.Op.Trim()))
                {
                    errors.Add(new FieldError($"conditions[{i}].op", "INVALID_OPERATOR"));
                }
            }
        }

        if (!OtRuleConstants.SupportedLogic.Contains(request.Logic.Trim().ToUpperInvariant()))
        {
            errors.Add(new FieldError("logic", "INVALID_LOGIC"));
        }

        if (!OtRuleConstants.SupportedActions.Contains(request.Action.Type.Trim()))
        {
            errors.Add(new FieldError("action.type", "INVALID_ACTION"));
        }

        if (request.Action.Type.Trim().Equals(OtRuleConstants.ActionSpecialQueue, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(request.Action.QueueName))
        {
            errors.Add(new FieldError("action.queue_name", "QUEUE_NAME_REQUIRED"));
        }

        return errors;
    }
}
