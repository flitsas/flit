namespace Flit.Admin.Domain.OtWebhooks;

public interface IOtWebhookSecretHasher
{
    string HashSecret(string secret);
}
