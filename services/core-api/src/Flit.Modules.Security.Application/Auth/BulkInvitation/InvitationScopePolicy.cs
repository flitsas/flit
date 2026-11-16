using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.BulkInvitation;

/// <summary>
/// Por qué ruta se invita (HU #14421, ADR-0077): define el tipo de destino que se admite y quién puede repartir
/// administradores. <see cref="Security"/> = <c>/security/invitations*</c> (SuperAdmin o AdminCompany, destino compañía u OT
/// para el SuperAdmin); <see cref="Ot"/> = <c>/admin/ot/users/invite*</c> (SuperAdmin, ot_admin o usuario del organismo con
/// «Usuarios» delegado, destino SIEMPRE un organismo de tránsito).
/// </summary>
public enum InvitationChannel
{
    Security = 0,
    Ot = 1,
}

/// <summary>
/// Quién invita y a dónde. Todo sale del JWT y del destino resuelto por el endpoint, nunca del archivo cargado.
/// </summary>
/// <param name="InviterUserId">Usuario autenticado que invita (claim <c>sub</c>).</param>
/// <param name="InviterTenantId">Tenant del JWT del invitador.</param>
/// <param name="InviterRoleCodes">TODOS los claims <c>role</c> del JWT (un usuario puede llevar varios).</param>
/// <param name="TargetTenantId">Tenant donde nacerán los usuarios invitados.</param>
/// <param name="Channel">Familia de rutas desde la que se invita.</param>
public sealed record InvitationScopeContext(
    Guid InviterUserId,
    Guid InviterTenantId,
    IReadOnlyCollection<string> InviterRoleCodes,
    Guid TargetTenantId,
    InvitationChannel Channel);

/// <summary>Códigos de motivo ESTABLES de la política (los consumen la API, el reporte por fila y la UI).</summary>
public static class InvitationScopeReasons
{
    /// <summary>El invitador no tiene un perfil que pueda invitar por esta ruta (fail-closed).</summary>
    public const string InviterNotAuthorized = "INVITER_NOT_AUTHORIZED";

    /// <summary>Un invitador que no es SuperAdmin intentó invitar fuera de su propio tenant.</summary>
    public const string TargetTenantOutOfScope = "TARGET_TENANT_OUT_OF_SCOPE";

    /// <summary>El tenant destino no existe.</summary>
    public const string TargetTenantNotFound = "TARGET_TENANT_NOT_FOUND";

    /// <summary>Perfil FLIT (el SuperAdmin invitando a su propio tenant): fuera de v1.</summary>
    public const string CannotInviteToOwnTenant = "CANNOT_INVITE_TO_OWN_TENANT";

    /// <summary>La ruta OT solo admite organismos de tránsito como destino.</summary>
    public const string TargetNotTransitOffice = "TARGET_NOT_TRANSIT_OFFICE";

    /// <summary>El rol no existe, no está activo o no es visible para el tenant destino.</summary>
    public const string RoleNotFound = "ROLE_NOT_FOUND";

    /// <summary>El valor de la columna Rol coincide con más de un rol (mismo nombre global y propio).</summary>
    public const string RoleAmbiguous = "ROLE_AMBIGUOUS";

    /// <summary>El rol SuperAdmin pertenece al perfil FLIT: no se asigna desde una compañía ni desde un organismo.</summary>
    public const string SuperAdminRoleNotAssignable = "SUPERADMIN_ROLE_NOT_ASSIGNABLE";

    /// <summary>El rol es de otro tipo de entidad que el destino (p. ej. rol de compañía en un organismo).</summary>
    public const string RoleTargetEntityTypeMismatch = "ROLE_TARGET_ENTITY_TYPE_MISMATCH";

    /// <summary>Solo un administrador puede repartir roles de administrador.</summary>
    public const string AdminRoleNotAssignable = "ADMIN_ROLE_NOT_ASSIGNABLE";
}

/// <param name="Id">Id del rol.</param>
/// <param name="Code">Código técnico (<c>ot_admin</c>, <c>gestor_tramites_ot</c>…).</param>
/// <param name="Name">Nombre visible: lo que ve el usuario en la plantilla.</param>
/// <param name="Assignable">¿Quien invita puede asignarlo en este contexto?</param>
/// <param name="DenyReason">Motivo estable (<see cref="InvitationScopeReasons"/>) cuando no es asignable.</param>
public sealed record InvitationRoleOption(Guid Id, string Code, string Name, bool Assignable, string? DenyReason);

/// <summary>
/// Resultado de evaluar el alcance. Fail-closed: <see cref="Allowed"/> solo es <c>true</c> si TODAS las comprobaciones pasan;
/// con <c>false</c> no hay roles (ni siquiera para mostrarlos).
/// </summary>
public sealed class InvitationScopeDecision
{
    private InvitationScopeDecision(
        bool allowed,
        string? denyReason,
        InvitationTargetKind? targetKind,
        IReadOnlyList<InvitationRoleOption> roles)
    {
        Allowed = allowed;
        DenyReason = denyReason;
        TargetKind = targetKind;
        Roles = roles;
    }

    public bool Allowed { get; }

    /// <summary>Motivo estable del rechazo del alcance (<see cref="InvitationScopeReasons"/>); <c>null</c> si está permitido.</summary>
    public string? DenyReason { get; }

    /// <summary>Tipo de entidad del destino; <c>null</c> si el alcance fue rechazado antes de resolverlo.</summary>
    public InvitationTargetKind? TargetKind { get; }

    /// <summary>Todos los roles visibles en el destino, cada uno con su decisión (permite explicar por qué se rechaza una fila).</summary>
    public IReadOnlyList<InvitationRoleOption> Roles { get; }

    /// <summary>Catálogo asignable: lo que ofrece la plantilla y lo único que el commit acepta.</summary>
    public IEnumerable<InvitationRoleOption> AssignableRoles => Roles.Where(r => r.Assignable);

    public static InvitationScopeDecision Denied(string reason, InvitationTargetKind? targetKind = null) =>
        new(false, reason, targetKind, []);

    public static InvitationScopeDecision Permitted(InvitationTargetKind targetKind, IReadOnlyList<InvitationRoleOption> roles) =>
        new(true, null, targetKind, roles);

    /// <summary>
    /// Roles cuyo nombre visible o código coincide (sin distinguir mayúsculas) con lo escrito en la fila. 0 = no existe,
    /// 1 = resuelto, &gt;1 = ambiguo.
    /// </summary>
    public IReadOnlyList<InvitationRoleOption> FindRoles(string? reference)
    {
        var value = reference?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return
        [
            .. Roles
                .Where(r => string.Equals(r.Name, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Code, value, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(r => r.Id),
        ];
    }
}

/// <summary>
/// ÚNICA fuente de la regla «qué roles puede asignar quien invita» para la invitación masiva (plantilla, dryRun y commit).
/// ADR-0077: los endpoints individuales repiten esta regla a mano y migrarán después; hasta entonces las pruebas de paridad
/// la mantienen alineada.
/// </summary>
public interface IInvitationScopePolicy
{
    /// <summary>Evalúa el alcance y resuelve el catálogo de roles asignables para ese contexto.</summary>
    Task<InvitationScopeDecision> ResolveAsync(InvitationScopeContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Implementación de <see cref="IInvitationScopePolicy"/>.
/// <list type="bullet">
/// <item>Ruta Security: SuperAdmin o AdminCompany. El SuperAdmin nunca invita a su propio tenant (perfil FLIT, fuera de v1);
/// los demás, solo a su propio tenant, que debe ser una compañía.</item>
/// <item>Ruta OT: SuperAdmin (a un organismo ≠ el suyo), ot_admin o usuario del organismo (Gestor OT) sobre SU organismo.</item>
/// <item>Roles: el rol SuperAdmin no se asigna jamás; el rol debe ser del tipo de entidad del destino; solo SuperAdmin,
/// AdminCompany (ruta Security) y ot_admin reparten roles de administrador, así que un Gestor OT nunca los ve.</item>
/// </list>
/// Cualquier caso no previsto se deniega (fail-closed).
/// </summary>
public sealed class InvitationScopePolicy(IInvitationTargetDirectory directory) : IInvitationScopePolicy
{
    private const string SuperAdminCode = "SuperAdmin";
    private const string AdminCompanyCode = "AdminCompany";
    private const string OtAdminCode = "ot_admin";
    private const string CompanyEntityType = "COMPANY";
    private const string TransitOfficeEntityType = "TRANSIT_OFFICE";

    public async Task<InvitationScopeDecision> ResolveAsync(InvitationScopeContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.InviterUserId == Guid.Empty || context.InviterTenantId == Guid.Empty)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.InviterNotAuthorized);
        }

        if (context.TargetTenantId == Guid.Empty)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.TargetTenantNotFound);
        }

        var roleCodes = new HashSet<string>(context.InviterRoleCodes ?? [], StringComparer.OrdinalIgnoreCase);
        var isSuperAdmin = roleCodes.Contains(SuperAdminCode);
        var isAdminCompany = roleCodes.Contains(AdminCompanyCode);
        var isOtAdmin = roleCodes.Contains(OtAdminCode);

        // Quién puede usar cada ruta (misma exigencia que las policies de los endpoints individuales).
        if (context.Channel == InvitationChannel.Security && !isSuperAdmin && !isAdminCompany)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.InviterNotAuthorized);
        }

        if (context.Channel is not (InvitationChannel.Security or InvitationChannel.Ot))
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.InviterNotAuthorized);
        }

        // Alcance de tenant: solo el SuperAdmin cruza tenants.
        if (!isSuperAdmin && context.TargetTenantId != context.InviterTenantId)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.TargetTenantOutOfScope);
        }

        // Perfil FLIT (SuperAdmin invitando a su propio tenant): fuera de v1.
        if (isSuperAdmin && context.TargetTenantId == context.InviterTenantId)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.CannotInviteToOwnTenant);
        }

        var target = await directory.GetTargetAsync(context.TargetTenantId, cancellationToken).ConfigureAwait(false);
        if (!target.Exists)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.TargetTenantNotFound);
        }

        if (context.Channel == InvitationChannel.Ot && target.Kind != InvitationTargetKind.TransitOffice)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.TargetNotTransitOffice, target.Kind);
        }

        // Un AdminCompany solo invita a una compañía. El SuperAdmin puede elegir compañía u organismo por la ruta Security.
        if (context.Channel == InvitationChannel.Security && !isSuperAdmin && target.Kind != InvitationTargetKind.Company)
        {
            return InvitationScopeDecision.Denied(InvitationScopeReasons.InviterNotAuthorized, target.Kind);
        }

        var canAssignAdministrators = context.Channel == InvitationChannel.Security
            ? isSuperAdmin || isAdminCompany
            : isSuperAdmin || isOtAdmin;
        var expectedEntityType = target.Kind == InvitationTargetKind.TransitOffice ? TransitOfficeEntityType : CompanyEntityType;

        var visible = await directory.ListVisibleRolesAsync(context.TargetTenantId, cancellationToken).ConfigureAwait(false);

        var roles = visible
            // Defensa en profundidad: un rol propio de OTRO tenant jamás es visible, aunque el directorio lo entregue.
            .Where(r => r.TenantId is null || r.TenantId == context.TargetTenantId)
            .Select(r => Evaluate(r, expectedEntityType, canAssignAdministrators))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return InvitationScopeDecision.Permitted(target.Kind, roles);
    }

    private static InvitationRoleOption Evaluate(InvitationRoleInfo role, string expectedEntityType, bool canAssignAdministrators)
    {
        // El SuperAdmin es del perfil FLIT: su TargetEntityType es COMPANY (el CHECK de BD no admite otro), así que la
        // coherencia de tipo no lo descarta — hay que excluirlo por código.
        if (string.Equals(role.Code, SuperAdminCode, StringComparison.OrdinalIgnoreCase))
        {
            return Denied(role, InvitationScopeReasons.SuperAdminRoleNotAssignable);
        }

        if (!string.Equals(role.TargetEntityType, expectedEntityType, StringComparison.Ordinal))
        {
            return Denied(role, InvitationScopeReasons.RoleTargetEntityTypeMismatch);
        }

        if (!canAssignAdministrators && AdministratorRoles.Any([role.Code]))
        {
            return Denied(role, InvitationScopeReasons.AdminRoleNotAssignable);
        }

        return new InvitationRoleOption(role.Id, role.Code, role.Name, true, null);
    }

    private static InvitationRoleOption Denied(InvitationRoleInfo role, string reason) =>
        new(role.Id, role.Code, role.Name, false, reason);
}

/// <summary>
/// El alcance del invitador no permite la operación (HU #14421): la API la traduce a 403 y NO se procesa ninguna fila.
/// </summary>
public sealed class InvitationScopeDeniedException(string reason) : Exception($"Alcance de invitación denegado: {reason}")
{
    /// <summary>Motivo estable (<see cref="InvitationScopeReasons"/>).</summary>
    public string Reason { get; } = reason;
}
