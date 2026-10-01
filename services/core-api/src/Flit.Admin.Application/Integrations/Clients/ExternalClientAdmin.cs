using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Flit.Admin.Domain.Integrations;

namespace Flit.Admin.Application.Integrations.Clients;

/// <summary>HU #13088 — alta de un cliente externo (secreto generado por el sistema).</summary>
public sealed record CreateExternalClientCommand(
    string? ClientId, string? DisplayName, string? Purpose, IReadOnlyList<string>? Scopes, Guid? Actor);

/// <summary>HU #13088 — edición; campos <c>null</c> = sin cambio.</summary>
public sealed record UpdateExternalClientCommand(
    Guid Id, string? DisplayName, string? Purpose, IReadOnlyList<string>? Scopes, bool? IsActive, bool? MustRotate, Guid? Actor);

/// <summary>El cliente y su secreto en claro, que se devuelve UNA sola vez y no se guarda en ningún sitio.</summary>
public sealed record ExternalClientSecretResult(ExternalClientView Client, string ClientSecret);

/// <summary>Códigos de error de la administración de clientes externos.</summary>
public static class ExternalClientAdminErrors
{
    public const string NotFound = "not_found";
    public const string ClientIdTaken = "client_id_taken";
    public const string InvalidClientId = "invalid_client_id";
    public const string InvalidDisplayName = "invalid_display_name";
    public const string InvalidPurpose = "invalid_purpose";
    public const string InvalidScopes = "invalid_scopes";
}

/// <summary>Validación y generación compartidas por los handlers (mismas reglas que las restricciones del DDL 125).</summary>
internal static partial class ExternalClientRules
{
    public const int DisplayNameMax = 120;
    public const int PurposeMax = 300;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdFormat();

    public static bool IsValidClientId(string? clientId) => clientId is not null && ClientIdFormat().IsMatch(clientId);

    public static bool IsValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;

    /// <summary>Al menos un permiso y todos del contrato. Devuelve la lista sin duplicados.</summary>
    public static IReadOnlyList<string>? NormalizeScopes(IReadOnlyList<string>? scopes)
    {
        if (scopes is null || scopes.Count == 0 || !scopes.All(ExternalScopes.EsValido))
        {
            return null;
        }

        return scopes.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Secreto de máquina: 32 bytes criptográficos en base64url (43 caracteres, sin símbolos problemáticos).</summary>
    public static string GenerateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

public sealed class ListExternalClientsHandler(IExternalClientRepository clients)
{
    public Task<IReadOnlyList<ExternalClientView>> HandleAsync(CancellationToken cancellationToken = default) =>
        clients.ListAsync(cancellationToken);
}

public sealed class GetExternalClientHandler(IExternalClientRepository clients)
{
    public Task<ExternalClientView?> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        clients.GetByIdAsync(id, cancellationToken);
}

/// <summary>HU #13088 AC1 — alta con secreto de un solo uso.</summary>
public sealed class CreateExternalClientHandler(IExternalClientRepository clients, IExternalClientSecretHasher hasher)
{
    public async Task<(ExternalClientSecretResult? Result, string? Error)> HandleAsync(
        CreateExternalClientCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ExternalClientRules.IsValidClientId(command.ClientId))
        {
            return (null, ExternalClientAdminErrors.InvalidClientId);
        }

        if (!ExternalClientRules.IsValidText(command.DisplayName, ExternalClientRules.DisplayNameMax))
        {
            return (null, ExternalClientAdminErrors.InvalidDisplayName);
        }

        if (!ExternalClientRules.IsValidText(command.Purpose, ExternalClientRules.PurposeMax))
        {
            return (null, ExternalClientAdminErrors.InvalidPurpose);
        }

        var scopes = ExternalClientRules.NormalizeScopes(command.Scopes);
        if (scopes is null)
        {
            return (null, ExternalClientAdminErrors.InvalidScopes);
        }

        var secret = ExternalClientRules.GenerateSecret();
        try
        {
            var created = await clients.CreateAsync(
                new NewExternalClient(
                    command.ClientId!, command.DisplayName!.Trim(), command.Purpose!.Trim(), hasher.Hash(secret), scopes, command.Actor),
                cancellationToken).ConfigureAwait(false);
            return (new ExternalClientSecretResult(created, secret), null);
        }
        catch (ExternalClientAlreadyExistsException)
        {
            return (null, ExternalClientAdminErrors.ClientIdTaken);
        }
    }
}

/// <summary>HU #13088 AC2 — desactivar, cambiar permisos, finalidad o nombre, forzar la rotación.</summary>
public sealed class UpdateExternalClientHandler(IExternalClientRepository clients, TimeProvider timeProvider)
{
    public async Task<(ExternalClientView? Result, string? Error)> HandleAsync(
        UpdateExternalClientCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.DisplayName is not null && !ExternalClientRules.IsValidText(command.DisplayName, ExternalClientRules.DisplayNameMax))
        {
            return (null, ExternalClientAdminErrors.InvalidDisplayName);
        }

        if (command.Purpose is not null && !ExternalClientRules.IsValidText(command.Purpose, ExternalClientRules.PurposeMax))
        {
            return (null, ExternalClientAdminErrors.InvalidPurpose);
        }

        IReadOnlyList<string>? scopes = null;
        if (command.Scopes is not null)
        {
            scopes = ExternalClientRules.NormalizeScopes(command.Scopes);
            if (scopes is null)
            {
                return (null, ExternalClientAdminErrors.InvalidScopes);
            }
        }

        var updated = await clients.UpdateAsync(
            command.Id,
            new ExternalClientChanges(command.DisplayName?.Trim(), command.Purpose?.Trim(), scopes, command.IsActive, command.MustRotate),
            command.Actor,
            timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        return updated is null ? (null, ExternalClientAdminErrors.NotFound) : (updated, null);
    }
}

/// <summary>
/// HU #13088 AC2 — regenera el secreto (se devuelve una vez). Por defecto el anterior sigue valiendo durante
/// la ventana de gracia (rotación planificada); con <c>revokePrevious</c> se anula al instante (filtración).
/// </summary>
public sealed class RegenerateExternalClientSecretHandler(
    IExternalClientRepository clients, IExternalClientSecretHasher hasher, TimeProvider timeProvider)
{
    public async Task<(ExternalClientSecretResult? Result, string? Error)> HandleAsync(
        Guid id, bool revokePrevious, Guid? actor, CancellationToken cancellationToken = default)
    {
        var secret = ExternalClientRules.GenerateSecret();
        var updated = await clients.ReplaceSecretAsync(
            id, hasher.Hash(secret), revokePrevious, actor, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        return updated is null
            ? (null, ExternalClientAdminErrors.NotFound)
            : (new ExternalClientSecretResult(updated, secret), null);
    }
}

/// <summary>HU #13088 AC2 — desbloqueo antes de que venzan los 15 minutos.</summary>
public sealed class UnlockExternalClientHandler(IExternalClientRepository clients, TimeProvider timeProvider)
{
    public async Task<(ExternalClientView? Result, string? Error)> HandleAsync(
        Guid id, Guid? actor, CancellationToken cancellationToken = default)
    {
        var updated = await clients.UnlockAsync(id, actor, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return updated is null ? (null, ExternalClientAdminErrors.NotFound) : (updated, null);
    }
}
