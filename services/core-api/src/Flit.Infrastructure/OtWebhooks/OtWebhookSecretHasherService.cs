using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Infrastructure.OtWebhooks;

internal sealed class OtWebhookSecretHasherService : IOtWebhookSecretHasher
{
    public string HashSecret(string secret) => OtWebhookSecretHasher.HashSecret(secret);
}
