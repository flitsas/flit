using System.Text.RegularExpressions;
using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Admin.Application.OtWebhooks.CreateOtWebhook;

/// <summary>Crea una suscripción webhook OT (HU #10216 AC1).</summary>
public sealed class CreateOtWebhookHandler
{
    private static readonly Regex HttpsUrlRegex = new(
        @"^https://",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IOtWebhookSubscriptionRepository _repository;
    private readonly IOtWebhookSecretHasher _secretHasher;

    public CreateOtWebhookHandler(
        IOtWebhookSubscriptionRepository repository,
        IOtWebhookSecretHasher secretHasher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _secretHasher = secretHasher ?? throw new ArgumentNullException(nameof(secretHasher));
    }

    public async Task<CreateOtWebhookResult> HandleAsync(
        CreateOtWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var errors = Validate(command.Request);
        if (errors.Count > 0)
        {
            return CreateOtWebhookResult.ValidationFailed(errors.ToArray());
        }

        var secretHash = _secretHasher.HashSecret(command.Request.Secret);
        var created = await _repository.CreateAsync(
            command.TenantId,
            command.Request.EventType.Trim(),
            command.Request.TargetUrl.Trim(),
            secretHash,
            command.CreatedBy,
            cancellationToken).ConfigureAwait(false);

        return CreateOtWebhookResult.Created(OtWebhookMapper.ToResponse(created));
    }

    private static List<FieldError> Validate(CreateOtWebhookRequest request)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(request.EventType)
            || !OtWebhookEventTypes.All.Contains(request.EventType.Trim()))
        {
            errors.Add(new FieldError("event_type", "INVALID_EVENT_TYPE"));
        }

        if (string.IsNullOrWhiteSpace(request.TargetUrl)
            || !HttpsUrlRegex.IsMatch(request.TargetUrl.Trim()))
        {
            errors.Add(new FieldError("target_url", "INVALID_TARGET_URL"));
        }

        if (string.IsNullOrWhiteSpace(request.Secret))
        {
            errors.Add(new FieldError("secret", "SECRET_REQUIRED"));
        }

        return errors;
    }
}
