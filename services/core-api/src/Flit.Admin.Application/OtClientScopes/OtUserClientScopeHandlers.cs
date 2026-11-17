using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.OtClientScopes;

/// <summary>Resultado de validar/guardar un alcance de compañías.</summary>
public enum SetOtUserClientScopeStatus
{
    Ok,
    /// <summary>Modo fuera de <c>todos</c>|<c>especificos</c>.</summary>
    InvalidMode,
    /// <summary><c>especificos</c> sin ninguna compañía.</summary>
    Empty,
    /// <summary>Alguna compañía no es cliente visible del OT.</summary>
    InvalidClient,
    /// <summary>Nadie cambia su propio alcance.</summary>
    Self,
    /// <summary>El usuario no es del OT.</summary>
    UserNotFound,
}

public sealed record SetOtUserClientScopeCommand(
    Guid OtTenantId,
    Guid? TransitOfficeIdOverride,
    Guid TargetUserId,
    Guid ActorUserId,
    string? Mode,
    IReadOnlyCollection<Guid>? ClientTenantIds);

/// <summary>
/// Define qué compañías cliente puede ver/gestionar un usuario del OT. La autorización por rol (solo Admin OT y
/// Super Admin) la impone el endpoint; aquí viven las reglas de negocio: no editarse a uno mismo, mínimo una
/// compañía en <c>especificos</c> y solo compañías que el OT ve.
/// </summary>
public sealed class SetOtUserClientScopeHandler(IOtUserClientScopeRepository repository)
{
    public async Task<SetOtUserClientScopeStatus> HandleAsync(
        SetOtUserClientScopeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!OtClientScopeModes.IsValid(command.Mode))
        {
            return SetOtUserClientScopeStatus.InvalidMode;
        }

        if (command.TargetUserId == command.ActorUserId)
        {
            return SetOtUserClientScopeStatus.Self;
        }

        var office = await repository
            .ResolveTransitOfficeIdOfUserAsync(command.OtTenantId, command.TargetUserId, cancellationToken)
            .ConfigureAwait(false);
        if (office is null)
        {
            return SetOtUserClientScopeStatus.UserNotFound;
        }

        var ids = (command.ClientTenantIds ?? []).Where(i => i != Guid.Empty).Distinct().ToList();
        if (command.Mode == OtClientScopeModes.Especificos)
        {
            if (ids.Count == 0)
            {
                return SetOtUserClientScopeStatus.Empty;
            }

            var visible = (await repository
                    .ListClientCompaniesAsync(command.OtTenantId, command.TransitOfficeIdOverride, cancellationToken)
                    .ConfigureAwait(false))
                .Select(c => c.TenantId)
                .ToHashSet();
            if (ids.Exists(i => !visible.Contains(i)))
            {
                return SetOtUserClientScopeStatus.InvalidClient;
            }
        }
        else
        {
            ids = [];
        }

        await repository
            .SetAsync(command.OtTenantId, office.Value, command.TargetUserId, command.Mode!, ids, command.ActorUserId, cancellationToken)
            .ConfigureAwait(false);
        return SetOtUserClientScopeStatus.Ok;
    }
}

/// <summary>Validación del alcance que acompaña a una invitación (mismas reglas, sin usuario destino todavía).</summary>
public sealed class ValidateOtInvitationClientScopeHandler(IOtUserClientScopeRepository repository)
{
    /// <returns><see cref="SetOtUserClientScopeStatus.Ok"/> y la lista a guardar (vacía = todos).</returns>
    public async Task<(SetOtUserClientScopeStatus Status, IReadOnlyList<Guid> ClientTenantIds)> HandleAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, string? mode, IReadOnlyCollection<Guid>? clientTenantIds,
        CancellationToken cancellationToken = default)
    {
        if (!OtClientScopeModes.IsValid(mode))
        {
            return (SetOtUserClientScopeStatus.InvalidMode, []);
        }

        if (mode == OtClientScopeModes.Todos)
        {
            return (SetOtUserClientScopeStatus.Ok, []);
        }

        var ids = (clientTenantIds ?? []).Where(i => i != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return (SetOtUserClientScopeStatus.Empty, []);
        }

        var visible = (await repository
                .ListClientCompaniesAsync(otTenantId, transitOfficeIdOverride, cancellationToken)
                .ConfigureAwait(false))
            .Select(c => c.TenantId)
            .ToHashSet();
        return ids.Exists(i => !visible.Contains(i))
            ? (SetOtUserClientScopeStatus.InvalidClient, [])
            : (SetOtUserClientScopeStatus.Ok, ids);
    }
}

public sealed class GetOtUserClientScopeHandler(IOtUserClientScopeRepository repository)
{
    /// <returns><c>null</c> si el usuario no es del OT (404).</returns>
    public async Task<OtUserClientScopeView?> HandleAsync(
        Guid otTenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var office = await repository
            .ResolveTransitOfficeIdOfUserAsync(otTenantId, userId, cancellationToken).ConfigureAwait(false);
        return office is null
            ? null
            : await repository.GetAsync(otTenantId, userId, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ListOtClientCompaniesHandler(IOtUserClientScopeRepository repository)
{
    public Task<IReadOnlyList<OtClientCompanyItem>> HandleAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, CancellationToken cancellationToken = default) =>
        repository.ListClientCompaniesAsync(otTenantId, transitOfficeIdOverride, cancellationToken);
}
