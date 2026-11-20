using Flit.Admin.Domain.Integrations;

namespace Flit.Admin.Application.Integrations.Clients;

/// <summary>
/// HU #14067 (Feature H, Épica #14021) — alta de una integración externa ligada a una compañía. El alcance lo fija el tipo
/// (<see cref="ExternalIntegrationTypes.AlcancesDe"/>): el superadministrador no elige permisos. El identificador lo genera
/// el sistema y el secreto se devuelve UNA sola vez.
/// </summary>
public sealed record CreateExternalIntegrationCommand(
    Guid CompanyId, string? Tipo, string? DisplayName, string? Purpose, Guid? Actor);

/// <summary>Acciones de ciclo de vida de una integración ligada a una compañía.</summary>
public enum ExternalIntegrationAction
{
    /// <summary>Bloqueo indefinido: no emite pases hasta desbloquearla. Sigue ocupando el cupo de «una activa por tipo».</summary>
    Block,

    /// <summary>Quita el bloqueo.</summary>
    Unblock,

    /// <summary>Baja lógica de uso (<c>is_active=false</c>): no emite pases y libera el cupo para dar de alta otra.</summary>
    Deactivate,
}

/// <summary>Códigos de error de la administración de integraciones por compañía.</summary>
public static class ExternalIntegrationAdminErrors
{
    public const string CompanyNotFound = "company_not_found";
    public const string NotFound = "integration_not_found";
    public const string InvalidType = "invalid_integration_type";

    /// <summary>409 del AC2: ya existe una integración activa del mismo tipo en la compañía.</summary>
    public const string AlreadyActive = "integracion_ya_activa";

    /// <summary>409: la integración está desactivada; hay que crear otra.</summary>
    public const string Inactive = "integracion_inactiva";

    /// <summary>409: la integración está bloqueada; hay que desbloquearla antes de rotar su secreto.</summary>
    public const string Blocked = "integracion_bloqueada";

    public const string InvalidDisplayName = ExternalClientAdminErrors.InvalidDisplayName;
    public const string InvalidPurpose = ExternalClientAdminErrors.InvalidPurpose;
}

public sealed class ListExternalIntegrationsHandler(IExternalClientRepository clients)
{
    /// <returns><c>null</c> si la compañía no existe.</returns>
    public async Task<IReadOnlyList<ExternalClientView>?> HandleAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await clients.CompanyExistsAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await clients.ListByCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>HU #14067 AC1 y AC2 — alta ligada a la compañía; una sola integración activa por tipo.</summary>
public sealed class CreateExternalIntegrationHandler(IExternalClientRepository clients, IExternalClientSecretHasher hasher)
{
    private const int MaxIntentosDeIdentificador = 3;

    public async Task<(ExternalClientSecretResult? Result, string? Error)> HandleAsync(
        CreateExternalIntegrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ExternalIntegrationTypes.EsPorCompania(command.Tipo))
        {
            return (null, ExternalIntegrationAdminErrors.InvalidType);
        }

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName) ? "Portal Persona Natural" : command.DisplayName;
        if (!ExternalClientRules.IsValidText(displayName, ExternalClientRules.DisplayNameMax))
        {
            return (null, ExternalIntegrationAdminErrors.InvalidDisplayName);
        }

        var purpose = string.IsNullOrWhiteSpace(command.Purpose)
            ? "Portal de Persona Natural: radicar y consultar trámites de personas naturales en FLIT 2 (Ley 1581)"
            : command.Purpose;
        if (!ExternalClientRules.IsValidText(purpose, ExternalClientRules.PurposeMax))
        {
            return (null, ExternalIntegrationAdminErrors.InvalidPurpose);
        }

        if (!await clients.CompanyExistsAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return (null, ExternalIntegrationAdminErrors.CompanyNotFound);
        }

        var secret = ExternalClientRules.GenerateSecret();
        var secretHash = hasher.Hash(secret);

        // El identificador es aleatorio (los identificadores no se reutilizan): una colisión es casi imposible, pero se reintenta.
        for (var intento = 0; intento < MaxIntentosDeIdentificador; intento++)
        {
            try
            {
                var created = await clients.CreateAsync(
                    new NewExternalClient(
                        GenerateClientId(command.Tipo!), displayName.Trim(), purpose.Trim(), secretHash,
                        ExternalIntegrationTypes.AlcancesDe(command.Tipo!), command.Actor,
                        command.Tipo!, command.CompanyId),
                    cancellationToken).ConfigureAwait(false);
                return (new ExternalClientSecretResult(created, secret), null);
            }
            catch (ExternalIntegrationAlreadyActiveException)
            {
                return (null, ExternalIntegrationAdminErrors.AlreadyActive);
            }
            catch (ExternalClientAlreadyExistsException)
            {
                // Se genera otro identificador.
            }
        }

        return (null, ExternalClientAdminErrors.ClientIdTaken);
    }

    private static string GenerateClientId(string tipo) =>
        $"{(tipo == ExternalIntegrationTypes.PersonaNatural ? "pn" : "int")}-{Guid.NewGuid():N}"[..19];
}

/// <summary>
/// HU #14067 AC3 — rota el secreto: el anterior sigue valiendo 24 horas (ventana de gracia del emisor del pase) salvo con
/// <c>revokePrevious</c>, que lo anula al instante.
/// </summary>
public sealed class RotateExternalIntegrationSecretHandler(
    IExternalClientRepository clients, IExternalClientSecretHasher hasher, TimeProvider timeProvider)
{
    public async Task<(ExternalClientSecretResult? Result, string? Error)> HandleAsync(
        Guid companyId, Guid id, bool revokePrevious, Guid? actor, CancellationToken cancellationToken = default)
    {
        var current = await clients.GetByCompanyAsync(companyId, id, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return (null, ExternalIntegrationAdminErrors.NotFound);
        }

        if (!current.IsActive)
        {
            return (null, ExternalIntegrationAdminErrors.Inactive);
        }

        // Rotar limpia el bloqueo: una integración bloqueada a propósito no se desbloquea por rotar.
        if (ExternalIntegrationRules.EstaBloqueadaIndefinidamente(current))
        {
            return (null, ExternalIntegrationAdminErrors.Blocked);
        }

        var secret = ExternalClientRules.GenerateSecret();
        var updated = await clients.ReplaceSecretAsync(
            id, hasher.Hash(secret), revokePrevious, actor, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        return updated is null
            ? (null, ExternalIntegrationAdminErrors.NotFound)
            : (new ExternalClientSecretResult(updated, secret), null);
    }
}

/// <summary>
/// HU #14067 AC4 — bloquear, desbloquear o desactivar. Una integración bloqueada o desactivada deja de obtener pases al
/// instante (el emisor valida <c>is_active</c> y <c>locked_until</c> en cada canje); el cambio queda en
/// <c>audit.audit_logs</c> por el trigger de la tabla, con <c>updated_by</c> sellado con el superadministrador.
/// </summary>
public sealed class ChangeExternalIntegrationStateHandler(IExternalClientRepository clients, TimeProvider timeProvider)
{
    public async Task<(ExternalClientView? Result, string? Error)> HandleAsync(
        Guid companyId, Guid id, ExternalIntegrationAction action, Guid? actor, CancellationToken cancellationToken = default)
    {
        var current = await clients.GetByCompanyAsync(companyId, id, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return (null, ExternalIntegrationAdminErrors.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        ExternalClientView? updated;
        switch (action)
        {
            case ExternalIntegrationAction.Deactivate:
                if (!current.IsActive)
                {
                    return (current, null); // idempotente
                }

                updated = await clients.UpdateAsync(id, new ExternalClientChanges(IsActive: false), actor, now, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ExternalIntegrationAction.Block:
                if (!current.IsActive)
                {
                    return (null, ExternalIntegrationAdminErrors.Inactive);
                }

                updated = await clients.BlockAsync(id, actor, now, cancellationToken).ConfigureAwait(false);
                break;
            case ExternalIntegrationAction.Unblock:
                if (!current.IsActive)
                {
                    return (null, ExternalIntegrationAdminErrors.Inactive);
                }

                updated = await clients.UnlockAsync(id, actor, now, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Acción desconocida.");
        }

        return updated is null ? (null, ExternalIntegrationAdminErrors.NotFound) : (updated, null);
    }
}
