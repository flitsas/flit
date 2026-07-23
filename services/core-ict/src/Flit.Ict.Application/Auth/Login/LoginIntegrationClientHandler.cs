using System.Text.Json;
using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Auth.Login;

/// <summary>
/// Autentica un cliente de integración (login ICT independiente). Mejora sobre v1: rate-limit por
/// credencial (intentos + lock temporal), rotación de contraseña y scopes. Espejo del LoginHandler
/// de core-api pero contra <c>ict.integration_clients</c> y con emisión de token propio.
/// Devuelve la tupla (resultado, error) al estilo de los handlers de core-api.
/// </summary>
public sealed class LoginIntegrationClientHandler(
    IIntegrationClientRepository clients,
    IIctPasswordHasher hasher,
    ITenantDirectory tenants,
    IIctJwtTokenIssuer issuer)
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public async Task<(LoginIntegrationClientResult? Result, string? Error)> HandleAsync(
        LoginIntegrationClientCommand command,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var client = await clients.FindByUsernameAsync(command.Username, ct);
        if (client is null)
        {
            // Trabajo dummy en tiempo constante para no filtrar la existencia del usuario.
            hasher.Verify(command.Password, null);
            return (null, "invalid_credentials");
        }

        if (!client.IsActive)
        {
            return (null, "inactive");
        }

        if (client.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            return (null, "locked");
        }

        if (!hasher.Verify(command.Password, client.PasswordHash))
        {
            client.FailedLoginAttempts++;
            if (client.FailedLoginAttempts >= MaxFailedAttempts)
            {
                client.LockedUntil = now.Add(LockDuration);
                client.FailedLoginAttempts = 0;
            }

            await clients.SaveAsync(client, ct);
            return (null, "invalid_credentials");
        }

        if (client.MustRotate)
        {
            return (new LoginIntegrationClientResult(Token: null, ExpiresInSeconds: 0, MustRotate: true), null);
        }

        var tenant = await tenants.GetAsync(client.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            return (null, "tenant_unavailable");
        }

        client.FailedLoginAttempts = 0;
        client.LockedUntil = null;
        client.LastLoginAt = now;
        await clients.SaveAsync(client, ct);

        var scopes = ParseScopes(client.Scopes);
        var issued = issuer.Issue(client.Id, tenant.Id, tenant.LegalName, scopes);
        return (new LoginIntegrationClientResult(issued.Token, issued.ExpiresInSeconds, MustRotate: false), null);
    }

    private static List<string> ParseScopes(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
