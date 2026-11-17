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
    /// <summary>El actor tiene alcance <c>especificos</c> y intenta dar <c>todos</c> o compañías fuera del suyo (403).</summary>
    ExceedsActor,
}

/// <summary>
/// Alcance del USUARIO que actúa (no del destinatario). Un actor restringido (<c>especificos</c>) no puede ampliar:
/// ni entregar <c>todos</c> ni compañías fuera de su propio alcance. Sin restricción (Admin OT sin alcance, Super Admin,
/// procesos sin usuario) devuelve <c>null</c> y nada cambia.
/// </summary>
public static class OtActorClientScope
{
    /// <returns><c>null</c> = sin restricción; una lista = solo esas compañías (vacía = ve nada, falla cerrado).</returns>
    public static async Task<IReadOnlyList<Guid>?> GetRestrictionAsync(
        IOtUserClientScopeRepository repository,
        IOtUserClientScopeResolver? resolver,
        Guid otTenantId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (resolver is null || actorUserId == Guid.Empty)
        {
            return null;
        }

        var office = await repository
            .ResolveTransitOfficeIdOfUserAsync(otTenantId, actorUserId, cancellationToken).ConfigureAwait(false);
        return office is null
            ? null
            : await resolver.GetRestrictionForUserAsync(actorUserId, office.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>¿Lo que se quiere entregar excede el alcance del actor? (<c>todos</c> siempre lo excede).</summary>
    public static bool Exceeds(IReadOnlyList<Guid>? actorRestriction, string? mode, IReadOnlyCollection<Guid> ids)
    {
        if (actorRestriction is null)
        {
            return false;
        }

        if (mode == OtClientScopeModes.Todos)
        {
            return true;
        }

        var allowed = actorRestriction.ToHashSet();
        return ids.Any(i => !allowed.Contains(i));
    }
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
public sealed class SetOtUserClientScopeHandler(
    IOtUserClientScopeRepository repository,
    IOtUserClientScopeResolver? actorScopeResolver = null)
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
        if (command.Mode == OtClientScopeModes.Especificos && ids.Count == 0)
        {
            return SetOtUserClientScopeStatus.Empty;
        }

        // Un actor restringido no puede ampliar: ni «todos» ni compañías fuera de su propio alcance.
        var actorRestriction = await OtActorClientScope
            .GetRestrictionAsync(repository, actorScopeResolver, command.OtTenantId, command.ActorUserId, cancellationToken)
            .ConfigureAwait(false);
        if (OtActorClientScope.Exceeds(actorRestriction, command.Mode, ids))
        {
            return SetOtUserClientScopeStatus.ExceedsActor;
        }

        if (command.Mode == OtClientScopeModes.Especificos)
        {
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
public sealed class ValidateOtInvitationClientScopeHandler(
    IOtUserClientScopeRepository repository,
    IOtUserClientScopeResolver? actorScopeResolver = null)
{
    /// <summary>
    /// Alcance que hereda el invitado cuando quien invita está restringido y no manda <c>clientScope</c>: exactamente
    /// los clientes de quien invita. <c>Restricted=false</c> = quien invita no está restringido (el invitado queda en
    /// <c>todos</c>, como siempre). Con <c>Restricted=true</c> y lista vacía (alcance huérfano) no hay nada que heredar:
    /// quien invita debe rechazar la invitación (falla cerrada), porque guardar «ninguna compañía» equivale a «todos».
    /// </summary>
    public async Task<(bool Restricted, IReadOnlyList<Guid> ClientTenantIds)> InheritFromActorAsync(
        Guid otTenantId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var restriction = await OtActorClientScope
            .GetRestrictionAsync(repository, actorScopeResolver, otTenantId, actorUserId, cancellationToken)
            .ConfigureAwait(false);
        return restriction is null ? (false, []) : (true, [.. restriction.Distinct()]);
    }

    /// <returns><see cref="SetOtUserClientScopeStatus.Ok"/> y la lista a guardar (vacía = todos).</returns>
    public Task<(SetOtUserClientScopeStatus Status, IReadOnlyList<Guid> ClientTenantIds)> HandleAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, string? mode, IReadOnlyCollection<Guid>? clientTenantIds,
        CancellationToken cancellationToken = default) =>
        HandleAsync(otTenantId, transitOfficeIdOverride, mode, clientTenantIds, Guid.Empty, cancellationToken);

    /// <param name="actorUserId">Quien invita: si está restringido no puede dar <c>todos</c> ni compañías fuera del suyo.</param>
    public async Task<(SetOtUserClientScopeStatus Status, IReadOnlyList<Guid> ClientTenantIds)> HandleAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, string? mode, IReadOnlyCollection<Guid>? clientTenantIds,
        Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!OtClientScopeModes.IsValid(mode))
        {
            return (SetOtUserClientScopeStatus.InvalidMode, []);
        }

        var ids = (clientTenantIds ?? []).Where(i => i != Guid.Empty).Distinct().ToList();
        if (mode == OtClientScopeModes.Especificos && ids.Count == 0)
        {
            return (SetOtUserClientScopeStatus.Empty, []);
        }

        var actorRestriction = await OtActorClientScope
            .GetRestrictionAsync(repository, actorScopeResolver, otTenantId, actorUserId, cancellationToken)
            .ConfigureAwait(false);
        if (OtActorClientScope.Exceeds(actorRestriction, mode, ids))
        {
            return (SetOtUserClientScopeStatus.ExceedsActor, []);
        }

        if (mode == OtClientScopeModes.Todos)
        {
            return (SetOtUserClientScopeStatus.Ok, []);
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

/// <summary>Compañías cliente elegibles para quien llama y si su propio alcance está restringido.</summary>
public sealed record OtClientCompaniesResult(IReadOnlyList<OtClientCompanyItem> Items, bool ActorRestricted);

public sealed class ListOtClientCompaniesHandler(
    IOtUserClientScopeRepository repository,
    IOtUserClientScopeResolver? actorScopeResolver = null)
{
    public Task<IReadOnlyList<OtClientCompanyItem>> HandleAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, CancellationToken cancellationToken = default) =>
        repository.ListClientCompaniesAsync(otTenantId, transitOfficeIdOverride, cancellationToken);

    /// <summary>
    /// Lo que el llamador puede REPARTIR: si tiene alcance <c>especificos</c>, solo sus propias compañías (no puede ampliar
    /// a otro usuario más allá de lo que él ve) y <c>ActorRestricted = true</c>; si no, todas las del OT.
    /// </summary>
    public async Task<OtClientCompaniesResult> HandleAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var items = await repository
            .ListClientCompaniesAsync(otTenantId, transitOfficeIdOverride, cancellationToken).ConfigureAwait(false);
        var restriction = await OtActorClientScope
            .GetRestrictionAsync(repository, actorScopeResolver, otTenantId, actorUserId, cancellationToken)
            .ConfigureAwait(false);
        if (restriction is null)
        {
            return new OtClientCompaniesResult(items, false);
        }

        var allowed = restriction.ToHashSet();
        return new OtClientCompaniesResult([.. items.Where(c => allowed.Contains(c.TenantId))], true);
    }
}
