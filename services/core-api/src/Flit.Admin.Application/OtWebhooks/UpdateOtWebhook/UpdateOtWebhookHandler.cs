using System.Text.RegularExpressions;
using Flit.Admin.Application.OtWebhooks.CreateOtWebhook;
using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Admin.Application.OtWebhooks.UpdateOtWebhook;

/// <summary>Hot-update de suscripción webhook OT (HU #10216 AC3).</summary>
public sealed class UpdateOtWebhookHandler
{
    private static readonly Regex HttpsUrlRegex = new(
        @"^https://",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IOtWebhookSubscriptionRepository _repository;

    public UpdateOtWebhookHandler(IOtWebhookSubscriptionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateOtWebhookResult> HandleAsync(
        UpdateOtWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        if (command.Request.TargetUrl is not null
            && (string.IsNullOrWhiteSpace(command.Request.TargetUrl)
                || !HttpsUrlRegex.IsMatch(command.Request.TargetUrl.Trim())))
        {
            return UpdateOtWebhookResult.ValidationFailed(
                new FieldError("target_url", "INVALID_TARGET_URL"));
        }

        var updated = await _repository.UpdateAsync(
            command.TenantId,
            command.SubscriptionId,
            command.Request.TargetUrl?.Trim(),
            command.Request.IsActive,
            command.ChangedBy,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? UpdateOtWebhookResult.NotFound()
            : UpdateOtWebhookResult.Updated(OtWebhookMapper.ToResponse(updated));
    }
}
