using Flit.Admin.Domain.Integrations;

namespace Flit.Admin.Application.Integrations.Auth;

/// <summary>
/// HU #13087 — canjea identificador y secreto de un cliente externo por un pase de 30 minutos.
/// Calca <c>LoginIntegrationClientHandler</c> de core-ict (bloqueo tras 5 fallos, rotación obligatoria)
/// con dos diferencias: el fallo se cuenta en una sola sentencia (dos peticiones simultáneas no pierden
/// intentos) y durante la ventana de gracia de una rotación también vale el secreto anterior.
/// </summary>
public sealed class IssueExternalClientTokenHandler(
    IExternalClientRepository clients,
    IExternalClientSecretHasher hasher,
    IExternalClientTokenIssuer issuer,
    ExternalClientAuthSettings settings,
    TimeProvider timeProvider)
{
    public async Task<IssueExternalClientTokenResult> HandleAsync(
        IssueExternalClientTokenCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!issuer.IsAvailable)
        {
            return new IssueExternalClientTokenResult(ExternalTokenStatus.Unavailable);
        }

        var secret = command.ClientSecret ?? string.Empty;
        var client = string.IsNullOrWhiteSpace(command.ClientId)
            ? null
            : await clients.GetCredentialsByClientIdAsync(command.ClientId, cancellationToken).ConfigureAwait(false);

        if (client is null || !client.IsActive)
        {
            // Trabajo de relleno: inexistente, inactivo y secreto incorrecto tardan lo mismo (AC2).
            hasher.Verify(secret, hasher.DummyHash);
            return new IssueExternalClientTokenResult(ExternalTokenStatus.InvalidClient);
        }

        var now = timeProvider.GetUtcNow();
        if (client.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            return new IssueExternalClientTokenResult(ExternalTokenStatus.Locked, RetryAfter: lockedUntil - now);
        }

        if (!SecretMatches(client, secret, now))
        {
            await clients.RegisterFailedAttemptAsync(
                client.Id,
                settings.MaxFailedAttempts,
                TimeSpan.FromMinutes(settings.LockoutMinutes),
                now,
                cancellationToken).ConfigureAwait(false);
            return new IssueExternalClientTokenResult(ExternalTokenStatus.InvalidClient);
        }

        // Después de verificar el secreto: sin él no se revela que el cliente debe rotar.
        if (client.MustRotate)
        {
            return new IssueExternalClientTokenResult(ExternalTokenStatus.RotationRequired);
        }

        await clients.RegisterTokenIssuedAsync(client.Id, now, cancellationToken).ConfigureAwait(false);

        // Solo los permisos del contrato viajan en el pase, aunque la tabla tuviera otro valor.
        var scopes = client.Scopes.Where(ExternalScopes.EsValido).Distinct(StringComparer.Ordinal).ToList();
        var token = issuer.Issue(client.ClientId, scopes);
        return new IssueExternalClientTokenResult(
            ExternalTokenStatus.Issued, token.Token, token.ExpiresInSeconds, scopes);
    }

    private bool SecretMatches(ExternalClientCredentials client, string secret, DateTimeOffset now)
    {
        if (hasher.Verify(secret, client.SecretHash))
        {
            return true;
        }

        return client.PreviousSecretHash is { } previous
            && client.SecretRotatedAt is { } rotatedAt
            && now < rotatedAt.AddHours(settings.SecretGraceHours)
            && hasher.Verify(secret, previous);
    }
}
