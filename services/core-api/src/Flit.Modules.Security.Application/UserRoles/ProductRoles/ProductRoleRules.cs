using Flit.Modules.Security.Domain.UserRoles.ProductRoles;

namespace Flit.Modules.Security.Application.UserRoles.ProductRoles;

/// <summary>Quién actúa, ya reverificado en la base por el adaptador (ADR-0089 regla 2).</summary>
public sealed record ProductActor(Guid UserId, bool IsSuperAdmin);

public enum ProductUserState
{
    Active,
    Invited,
    Suspended,
}

public enum ProductUserAction
{
    ChangeRole,
    Suspend,
    Reactivate,
}

public enum NoActionsReason
{
    None,
    Self,
    AdminCompany,
    SuperAdminOnly,
    Invitation,
    AccountInactive,
}

/// <summary>Fila del listado de usuarios de un producto, con las acciones que ESTE actor puede hacer sobre ella.</summary>
public sealed record ProductUserRow(
    Guid Id,
    bool IsInvitation,
    string Name,
    string Email,
    string RoleCode,
    ProductUserState State,
    IReadOnlyList<ProductUserAction> Actions,
    NoActionsReason Reason);

/// <summary>
/// ADR-0089 (HU #14587) — reglas que core-api vuelve a aplicar sobre los usuarios de un producto (contrato §3.4, SP-10 a
/// SP-13). Puras: sin E/S. Los roles asignables son SOLO <c>usuario_&lt;producto&gt;</c> y <c>admin_&lt;producto&gt;</c>,
/// por código (SP-10); el admin del producto solo lo da o lo quita el SuperAdmin (SP-11=a); AdminCompany y SuperAdmin no
/// se tocan desde el producto y nadie actúa sobre sí mismo (SP-12).
/// </summary>
public static class ProductRoleRules
{
    /// <summary>Único producto con módulo de usuarios en esta ola: el scope del cliente lo ata.</summary>
    public const string Diagnostico = "diagnostico";

    public static string UserRole(string productCode) => "usuario_" + productCode;

    public static string AdminRole(string productCode) => "admin_" + productCode;

    /// <summary>Permiso del manifiesto que habilita a un admin del producto a gestionar usuarios (SP-17).</summary>
    public static string ManagePermission(string productCode) => productCode + ".usuarios.gestionar";

    public static bool IsAllowedRole(string productCode, string? roleCode) =>
        string.Equals(roleCode, UserRole(productCode), StringComparison.Ordinal)
        || string.Equals(roleCode, AdminRole(productCode), StringComparison.Ordinal);

    /// <summary>CR-1: solo se ve (y se toca) quien tiene un rol de la lista blanca, vigente o guardado en la marca.</summary>
    public static bool IsVisible(string productCode, ProductMemberSnapshot member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.CurrentRole is { } actual
            ? IsAllowedRole(productCode, actual.Code)
            : member.SuspendedRole is { } guardado && IsAllowedRole(productCode, guardado.Code);
    }

    public static IReadOnlyList<string> AssignableRoles(ProductActor actor, string productCode)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.IsSuperAdmin ? [UserRole(productCode), AdminRole(productCode)] : [UserRole(productCode)];
    }

    public static void EnsureAllowedRole(string productCode, string? roleCode)
    {
        if (!IsAllowedRole(productCode, roleCode))
            throw Fail(ProductRoleErrorCodes.RolNoPermitido, ProductRoleErrorKind.InvalidArgument, "El rol no es un rol asignable del producto.");
    }

    /// <summary>SP-11=a: solo el SuperAdmin da el rol admin del producto.</summary>
    public static void EnsureCanGrant(ProductActor actor, string productCode, string roleCode)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsSuperAdmin && string.Equals(roleCode, AdminRole(productCode), StringComparison.Ordinal))
            throw Fail(ProductRoleErrorCodes.RolNoAsignable, ProductRoleErrorKind.PermissionDenied, "Solo el SuperAdmin asigna el rol de administrador del producto.");
    }

    public static void EnsureNotSelf(ProductActor actor, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId == userId)
            throw Fail(ProductRoleErrorCodes.AccionSobreSiMismo, ProductRoleErrorKind.InvalidArgument, "No puede cambiar su propio acceso.");
    }

    /// <summary>AdminCompany y SuperAdmin: su rol del producto es espejo y solo cambia en el hub.</summary>
    public static void EnsureNotProtected(ProductMemberSnapshot member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.IsAdminCompany || member.IsSuperAdmin)
            throw Fail(ProductRoleErrorCodes.ObjetivoProtegido, ProductRoleErrorKind.PermissionDenied, "El acceso de este usuario se gestiona en la plataforma.");
    }

    /// <summary>
    /// SP-11=a / CR-1: un admin del producto solo actúa sobre usuarios con el rol de usuario del producto (exactamente
    /// <c>usuario_&lt;producto&gt;</c>, de la lista blanca); ni sobre otro admin ni sobre un rol propio de la compañía.
    /// </summary>
    public static void EnsureCanTouch(ProductActor actor, string productCode, ProductMemberSnapshot member)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(member);
        if (!actor.IsSuperAdmin && !string.Equals(RoleOf(member), UserRole(productCode), StringComparison.Ordinal))
            throw Fail(ProductRoleErrorCodes.RolNoAsignable, ProductRoleErrorKind.PermissionDenied, "Solo el SuperAdmin cambia el acceso de un administrador del producto.");
    }

    /// <summary>¿Quitarle el admin a este usuario deja la empresa sin admin del producto activo?</summary>
    public static void EnsureNotLastAdmin(string productCode, string? currentRoleCode, int otherActiveAdmins)
    {
        if (string.Equals(currentRoleCode, AdminRole(productCode), StringComparison.Ordinal) && otherActiveAdmins <= 0)
            throw Fail(ProductRoleErrorCodes.UltimoAdministrador, ProductRoleErrorKind.FailedPrecondition, "La compañía quedaría sin administrador del producto.");
    }

    public static ProductUserRow RowFor(ProductActor actor, string productCode, ProductMemberSnapshot member)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(member);
        var state = member.CurrentRole is not null ? ProductUserState.Active : ProductUserState.Suspended;
        var (actions, reason) = ActionsFor(actor, productCode, member, state);
        return new ProductUserRow(member.UserId, false, member.DisplayName, member.Email, RoleOf(member) ?? string.Empty, state, actions, reason);
    }

    public static ProductUserRow RowFor(ProductInvitationSnapshot invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        return new ProductUserRow(invitation.InvitationId, true, invitation.FullName, invitation.Email, invitation.RoleCode,
            ProductUserState.Invited, [], NoActionsReason.Invitation);
    }

    private static (IReadOnlyList<ProductUserAction> Actions, NoActionsReason Reason) ActionsFor(
        ProductActor actor, string productCode, ProductMemberSnapshot member, ProductUserState state)
    {
        if (member.UserId == actor.UserId)
            return ([], NoActionsReason.Self);
        if (member.IsAdminCompany || member.IsSuperAdmin)
            return ([], NoActionsReason.AdminCompany);
        if (!member.AccountActive)
            return ([], NoActionsReason.AccountInactive);
        if (!actor.IsSuperAdmin && !string.Equals(RoleOf(member), UserRole(productCode), StringComparison.Ordinal))
            return ([], NoActionsReason.SuperAdminOnly);
        if (state == ProductUserState.Suspended)
            return ([ProductUserAction.Reactivate], NoActionsReason.None);

        // Un admin del producto solo puede dar el rol de usuario, que es el que ya tiene: cambiar de rol no le sirve.
        return actor.IsSuperAdmin
            ? ([ProductUserAction.ChangeRole, ProductUserAction.Suspend], NoActionsReason.None)
            : ([ProductUserAction.Suspend], NoActionsReason.None);
    }

    private static string? RoleOf(ProductMemberSnapshot member) => member.CurrentRole?.Code ?? member.SuspendedRole?.Code;

    internal static ProductRoleRuleException Fail(string code, ProductRoleErrorKind kind, string message) => new(code, kind, message);

    internal static ProductRoleRuleException NotFound() =>
        Fail(ProductRoleErrorCodes.UsuarioNoEncontrado, ProductRoleErrorKind.NotFound, "Usuario no encontrado.");

    /// <summary>
    /// SP-13: el objetivo tiene que ser de la empresa y tener o haber tenido un rol del producto de la lista blanca; si no,
    /// NOT_FOUND (mismo error en todos los casos). El id de una invitación pendiente del producto responde INVITACION_PENDIENTE.
    /// </summary>
    internal static async Task<ProductMemberSnapshot> LoadTargetAsync(
        IProductRoleRepository repo, Guid tenantId, Guid userId, string productCode, CancellationToken ct)
    {
        var member = await repo.GetMemberAsync(tenantId, userId, productCode, ct).ConfigureAwait(false);
        if (member is not null && IsVisible(productCode, member))
            return member;

        // CR-1: un rol propio de la compañía (fuera de la lista blanca) no existe para el producto: NOT_FOUND.
        if (await repo.GetPendingInvitationAsync(tenantId, userId, productCode, ct).ConfigureAwait(false) is { } invitacion
            && IsAllowedRole(productCode, invitacion.RoleCode))
            throw Fail(ProductRoleErrorCodes.InvitacionPendiente, ProductRoleErrorKind.FailedPrecondition, "Es una invitación pendiente: se gestiona cuando la persona active su cuenta.");

        throw NotFound();
    }
}

/// <summary>Resultado de una operación sobre la fila: antes y después, para la auditoría (SP-16).</summary>
public sealed record ProductRoleOutcome(ProductUserRow Row, string? RoleBefore, string? RoleAfter, bool Changed);
