using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Auth.Rotate;

/// <summary>
/// Rota la contraseña de un cliente de integración. Verifica la contraseña actual (mismo rate-limit que el
/// login), valida la nueva (longitud mínima y distinta de la actual), guarda el hash anterior en
/// <c>previous_password_hash</c>, sella <c>password_changed_at</c> y limpia <c>must_rotate</c>. Cierra el
/// callejón sin salida en que <c>must_rotate</c> bloqueaba el login sin dar forma de rotar (plan §A.5).
/// </summary>
public sealed class RotateIntegrationClientPasswordHandler(
    IIntegrationClientRepository clients,
    IIctPasswordHasher hasher)
{
    private const int MaxFailedAttempts = 5;
    private const int MinPasswordLength = 8;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public async Task<(bool Ok, string? Error)> HandleAsync(
        RotateIntegrationClientPasswordCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.NewPassword) || command.NewPassword.Length < MinPasswordLength)
        {
            return (false, "weak_password");
        }

        var now = DateTime.UtcNow;
        var client = await clients.FindByUsernameAsync(command.Username, ct);
        if (client is null)
        {
            hasher.Verify(command.CurrentPassword, null); // trabajo dummy: no filtrar existencia
            return (false, "invalid_credentials");
        }

        if (!client.IsActive)
        {
            return (false, "inactive");
        }

        if (client.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            return (false, "locked");
        }

        if (!hasher.Verify(command.CurrentPassword, client.PasswordHash))
        {
            client.FailedLoginAttempts++;
            if (client.FailedLoginAttempts >= MaxFailedAttempts)
            {
                client.LockedUntil = now.Add(LockDuration);
                client.FailedLoginAttempts = 0;
            }

            await clients.SaveAsync(client, ct);
            return (false, "invalid_credentials");
        }

        if (string.Equals(command.NewPassword, command.CurrentPassword, StringComparison.Ordinal))
        {
            return (false, "same_password");
        }

        client.PreviousPasswordHash = client.PasswordHash;
        client.PasswordHash = hasher.Hash(command.NewPassword);
        client.PasswordChangedAt = now;
        client.MustRotate = false;
        client.FailedLoginAttempts = 0;
        client.LockedUntil = null;
        await clients.SaveAsync(client, ct);

        return (true, null);
    }
}
