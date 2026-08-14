using System.Globalization;
using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Application.Auth.CancelInvitation;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.ResendInvitation;
using Flit.Modules.Security.Application.Modules;
using Flit.Modules.Security.Application.UserManagement.DeleteUser;
using Flit.Modules.Security.Application.UserManagement.SuspendUser;
using Flit.Modules.Security.Application.UserManagement.UnsuspendUser;
using Flit.Modules.Security.Application.UserManagement.UpdateUser;
using Flit.Modules.Security.Application.UserRoles;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IRoleRepository = Flit.Modules.Security.Domain.Roles.IRoleRepository;
using AuthRoleNotFoundException = Flit.Modules.Security.Domain.Auth.RoleNotFoundException;

namespace Flit.Api.Endpoints;

public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/security").RequireAuthorization();

        // HU #10175 — crear invitación por email (rol opcional)
        // Fase 3 fix: SuperAdmin puede especificar TargetTenantId para invitar a otro tenant
        group.MapPost("/invitations", async (
            [FromBody] CreateInvitationRequest request,
            ClaimsPrincipal caller,
            CreateInvitationHandler handler,
            FlitDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var invitedBy))
                return Results.Unauthorized();

            // Multi-rol (HU #10506): FindFirstValue solo evalúa el primer claim "role" del JWT,
            // en orden no determinístico — se evalúan TODOS los claims de ese tipo (fix post-review #10504).
            var isSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            Guid targetTenantId;
            IReadOnlyList<Guid> roleIds;

            // Un usuario tiene UN rol: lo que define lo que puede hacer son los permisos de ese
            // rol, no acumular roles. RoleIds sigue siendo lista por compatibilidad con la API
            // de la HU #10506, pero solo se admite un elemento.
            if ((request.RoleIds ?? []).Distinct().Count() > 1)
                return Results.Json(
                    new ErrorResponse(
                        "SINGLE_ROLE_ONLY",
                        "Un usuario solo puede tener un rol. Selecciona uno."),
                    statusCode: StatusCodes.Status400BadRequest);

            if (isSuperAdmin)
            {
                var requestedRoleIds = (request.RoleIds ?? []).Distinct().ToList();

                // ── Perfil FLIT ────────────────────────────────────────────────────────────
                // Invitar a otro miembro del equipo interno de FLIT. No lleva compañía ni OT:
                // el usuario vive en el tenant interno del propio SuperAdmin, que es justo el
                // caso que CANNOT_INVITE_TO_OWN_TENANT bloqueaba y que dejaba el perfil FLIT
                // sin ninguna ruta de creación. El rol SuperAdmin es transversal a la
                // plataforma; no tiene target_entity_type propio (el CHECK de BD solo admite
                // COMPANY | TRANSIT_OFFICE), así que vive con COMPANY — ver UserProfiles.
                var superAdminRole = await db.Roles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        r => r.Code == AdminAuthorization.SuperAdminRole && r.IsActive && r.DeletedAt == null,
                        cancellationToken);

                var isFlitInvite = superAdminRole is not null && requestedRoleIds.Contains(superAdminRole.Id);

                if (isFlitInvite)
                {
                    if (request.TargetTenantId is { } explicitTenant && explicitTenant != callerTenantId)
                        return Results.Json(
                            new ErrorResponse(
                                "FLIT_PROFILE_HAS_NO_TENANT",
                                "El perfil FLIT no pertenece a una compañía ni a un organismo de tránsito."),
                            statusCode: StatusCodes.Status400BadRequest);

                    targetTenantId = callerTenantId;
                    roleIds = [superAdminRole!.Id];
                }
                else
                {
                    // Perfiles Gestor / OT: el SuperAdmin SÍ debe decir a qué tenant invita.
                    if (!request.TargetTenantId.HasValue)
                        return Results.Json(
                            new ErrorResponse("TARGET_TENANT_REQUIRED", "El SuperAdmin debe especificar la empresa destino."),
                            statusCode: StatusCodes.Status400BadRequest);

                    if (request.TargetTenantId.Value == callerTenantId)
                        return Results.Json(
                            new ErrorResponse("CANNOT_INVITE_TO_OWN_TENANT", "El SuperAdmin no puede invitar usuarios a su propio tenant."),
                            statusCode: StatusCodes.Status400BadRequest);

                    targetTenantId = request.TargetTenantId.Value;

                    var isOtTenant = await db.TransitOfficeProfiles
                        .AsNoTracking()
                        .AnyAsync(p => p.TenantId == targetTenantId, cancellationToken);
                    var expectedTargetEntityType = isOtTenant ? TenantTypes.TransitOffice : TenantTypes.Company;

                    if (requestedRoleIds.Count == 0)
                    {
                        // Sin selección explícita se conserva el comportamiento histórico: rol de
                        // sistema forzado según el tipo de tenant destino. Mantiene compatibles a
                        // los clientes que nunca enviaron roleIds en esta rama.
                        var targetRoleCode = isOtTenant
                            ? AdminAuthorization.OtAdminRole
                            : AdminAuthorization.AdminCompanyRole;

                        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id),
                        // así que el rol de sistema se resuelve por Code únicamente (una sola fila por
                        // (code, target_entity_type) en todo el sistema).
                        var adminRole = await db.Roles
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.Code == targetRoleCode && r.IsActive && r.DeletedAt == null, cancellationToken);

                        if (adminRole is null)
                            return Results.Json(
                                new ErrorResponse(
                                    "ADMIN_ROLE_NOT_FOUND",
                                    isOtTenant
                                        ? "El organismo de tránsito destino no tiene configurado el rol ot_admin. Verifica que se creó correctamente."
                                        : "La empresa destino no tiene configurado el rol AdminCompany. Verifica que la empresa se creó correctamente."),
                                statusCode: StatusCodes.Status409Conflict);

                        roleIds = [adminRole.Id];
                    }
                    else
                    {
                        // Selección explícita: se valida que cada rol exista, esté activo y aplique
                        // al tipo de tenant destino, para no crear asignaciones incoherentes que
                        // AssignRoleHandler rechazaría después (RoleTargetEntityTypeMismatch).
                        var selectedRoles = await db.Roles
                            .AsNoTracking()
                            .Where(r => requestedRoleIds.Contains(r.Id) && r.IsActive && r.DeletedAt == null)
                            .ToListAsync(cancellationToken);

                        if (selectedRoles.Count != requestedRoleIds.Count)
                            return Results.Json(
                                new ErrorResponse("ROLE_NOT_FOUND", "Alguno de los roles seleccionados no existe o está inactivo."),
                                statusCode: StatusCodes.Status404NotFound);

                        if (selectedRoles.Exists(r => r.TargetEntityType != expectedTargetEntityType))
                            return Results.Json(
                                new ErrorResponse(
                                    "ROLE_TARGET_ENTITY_TYPE_MISMATCH",
                                    isOtTenant
                                        ? "Alguno de los roles seleccionados no aplica a un organismo de tránsito."
                                        : "Alguno de los roles seleccionados no aplica a una compañía."),
                                statusCode: StatusCodes.Status409Conflict);

                        roleIds = requestedRoleIds;
                    }
                }
            }
            else
            {
                targetTenantId = callerTenantId;
                // HU #10506 AC4/AC5: roleIds reemplaza el RoleId? nullable — seleccionar al
                // menos un rol es OBLIGATORIO (validado por el handler → NoRolesSelectedException).
                roleIds = request.RoleIds ?? [];

                // El rol SuperAdmin pertenece solo al perfil FLIT. Su TargetEntityType es COMPANY
                // (el CHECK de BD no admite un valor propio), así que ninguna de las validaciones
                // de coherencia lo descarta: sin esto un AdminCompany podría mandarlo en roleIds
                // y crear un SuperAdmin dentro de su propia compañía.
                if (await ContainsSuperAdminRoleAsync(db, roleIds, cancellationToken))
                    return SuperAdminRoleNotAssignable();
            }

            try
            {
                var result = await handler.HandleAsync(
                    new CreateInvitationCommand(targetTenantId, request.Email, request.FullName ?? string.Empty, roleIds, invitedBy),
                    cancellationToken);

                return Results.Created(
                    $"/api/v1/security/invitations/{result.InvitationId}",
                    new InvitationCreatedResponse(result.InvitationId, result.Email, result.EmailSent));
            }
            catch (NoRolesSelectedException)
            {
                return Results.Json(
                    new ErrorResponse("NO_ROLES_SELECTED", "Debes seleccionar al menos un rol para invitar al usuario."),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AuthRoleNotFoundException)
            {
                return Results.Json(
                    new ErrorResponse("ROLE_NOT_FOUND", "El rol especificado no existe en el tenant."),
                    statusCode: StatusCodes.Status404NotFound);
            }
            catch (InvitationAlreadyPendingException)
            {
                return Results.Json(
                    new ErrorResponse("INVITATION_ALREADY_PENDING", UserEmailConflictMessages.EmailAlreadyInUse),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (UserAlreadyExistsException)
            {
                return Results.Json(
                    new ErrorResponse("USER_ALREADY_EXISTS", UserEmailConflictMessages.EmailAlreadyInUse),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (UserEmailBelongsToDeletedAccountException)
            {
                // HU #10623 AC4 — el correo pertenece a una cuenta soft-deleted.
                // HU #11550 — mensaje visible unificado con los otros dos conflictos de correo;
                // el código de error se mantiene distinto para no perder trazabilidad.
                return Results.Json(
                    new ErrorResponse("EMAIL_BELONGS_TO_DELETED_USER", UserEmailConflictMessages.EmailAlreadyInUse),
                    statusCode: StatusCodes.Status409Conflict);
            }
        }).AddEndpointFilter(new AdminAuditFilter(
            AuditVocabulary.Modules.Users, AuditVocabulary.Operations.Invite, "invitation", "INVITATION"));

        // HU #10625 — reenviar invitación pendiente: SIEMPRE regenera el token de activación
        // (el enlace anterior queda invalidado) y reenvía el correo. Mismo alcance que crear
        // invitaciones: SuperAdmin puede reenviar cualquier invitación; AdminCompany solo las
        // de su propio tenant.
        group.MapPost("/invitations/{invitationId:guid}/resend", async (
            Guid invitationId,
            ClaimsPrincipal caller,
            ResendInvitationHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var resentBy))
                return Results.Unauthorized();

            var isSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            // SuperAdmin puede reenviar cualquier invitación del sistema (sin restricción de
            // tenant); AdminCompany solo las de su propio tenant (mismo alcance que /invitations).
            Guid? scopeTenantId = isSuperAdmin ? null : callerTenantId;

            try
            {
                var result = await handler.HandleAsync(
                    new ResendInvitationCommand(invitationId, scopeTenantId, resentBy),
                    cancellationToken);

                return Results.Ok(new InvitationCreatedResponse(result.InvitationId, result.Email, result.EmailSent));
            }
            catch (InvitationNotFoundException)
            {
                return Results.Json(
                    new ErrorResponse("INVITATION_NOT_FOUND", "La invitación no existe o no pertenece a tu alcance."),
                    statusCode: StatusCodes.Status404NotFound);
            }
            catch (InvitationNotPendingException)
            {
                return Results.Json(
                    new ErrorResponse("INVITATION_NOT_PENDING", "La invitación ya no está pendiente (fue aceptada o cancelada)."),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (ResendCooldownActiveException ex)
            {
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.TotalSeconds));
                httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return Results.Json(
                    new ResendCooldownResponse(
                        "RESEND_COOLDOWN_ACTIVE",
                        $"Debes esperar antes de reenviar esta invitación de nuevo. Intenta en {retryAfterSeconds} segundos.",
                        retryAfterSeconds),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
        }).AddEndpointFilter(new AdminAuditFilter(
            AuditVocabulary.Modules.Users, AuditVocabulary.Operations.ResendInvite, "invitation",
            "INVITATION", "invitationId"));

        // HU #10627 — cancelar invitación pendiente. Distinto de reenviar (HU #10625): anula la
        // invitación en vez de reenviar el correo. Mismo patrón de alcance que POST /invitations:
        // SuperAdmin puede cancelar la invitación de cualquier tenant; AdminCompany solo las de
        // su propio tenant.
        group.MapDelete("/invitations/{invitationId:guid}", async (
            Guid invitationId,
            ClaimsPrincipal caller,
            CancelInvitationHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var cancelledBy))
                return Results.Unauthorized();

            var isSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            // SuperAdmin no restringe por tenant (alcance global); AdminCompany solo su propio tenant.
            Guid? scopeTenantId = isSuperAdmin ? null : callerTenantId;

            try
            {
                await handler.HandleAsync(
                    new CancelInvitationCommand(invitationId, scopeTenantId, cancelledBy),
                    cancellationToken);

                return Results.NoContent();
            }
            catch (InvitationNotFoundException)
            {
                return Results.Json(
                    new ErrorResponse("INVITATION_NOT_FOUND", "La invitación no existe o no pertenece a tu alcance."),
                    statusCode: StatusCodes.Status404NotFound);
            }
            catch (InvitationNotPendingException)
            {
                return Results.Json(
                    new ErrorResponse("INVITATION_NOT_PENDING", "La invitación ya no está pendiente (fue aceptada o cancelada previamente)."),
                    statusCode: StatusCodes.Status409Conflict);
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Users, AuditVocabulary.Operations.Delete, "invitation",
              "INVITATION", "invitationId"));

        // GET /security/modules — módulos y acciones accesibles al caller según sus permisos JWT.
        // RBAC puro (HU #10664): los módulos son transversales (sin habilitación por empresa). El
        // caller SuperAdmin (includeAll=true) ve todos los módulos activos — lo usa el constructor de
        // roles; el caller tenant ve solo los módulos cuyos slugs están en sus permisos.
        group.MapGet("/modules", async (
            ClaimsPrincipal caller,
            ListAccessibleModulesHandler handler,
            CancellationToken ct) =>
        {
            // Multi-rol (HU #10506): FindFirstValue solo evalúa el primer claim "role" del JWT,
            // en orden no determinístico — se evalúan TODOS los claims de ese tipo (fix post-review #10504).
            var isSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
            var permissions = caller.FindAll("permissions").Select(c => c.Value).ToList();

            var modules = await handler.HandleAsync(permissions, isSuperAdmin, ct);
            return Results.Ok(modules);
        }).WithName("ListAccessibleModules");

        // AC5 — GET /roles — lista roles del catálogo global aplicable al tenant del caller
        // (HU #10164 original; HU #10505 lo migra a filtrar por target_entity_type en vez de
        // tenant_id — security.roles ya no tiene esa columna). Se resuelve COMPANY | TRANSIT_OFFICE
        // igual que en /invitations: por presencia de TransitOfficeProfile en el tenant del caller.
        group.MapGet("/roles", async (
            ClaimsPrincipal caller,
            IRoleRepository roleRepo,
            FlitDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var isOtTenant = await db.TransitOfficeProfiles
                .AsNoTracking()
                .AnyAsync(p => p.TenantId == tenantId, cancellationToken);
            var targetEntityType = isOtTenant ? "TRANSIT_OFFICE" : "COMPANY";

            // Fix 1 (post-review #10504): este endpoint es tenant-facing (checklist de invitación
            // de AdminCompany/OtAdmin) y NO debe listar roles inactivos ni el rol de sistema
            // SuperAdmin. NO tocar ListByTargetEntityTypeAsync ni ListRolesHandler — ese mismo
            // método lo usa la pantalla RBAC de SuperAdmin, que sí necesita ver TODOS los roles.
            var roles = (await roleRepo.ListByTargetEntityTypeAsync(targetEntityType, cancellationToken))
                .Where(r => r.IsActive && !string.Equals(r.Code, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Results.Ok(roles);
        });

        // HU #10508 AC2: la gobernanza de roles (crear/editar/eliminar) es EXCLUSIVA de
        // SuperAdmin vía /api/v1/superadmin/roles* (ver SecurityRolesEndpoints). AdminCompany y
        // OtAdmin conservan únicamente el GET de arriba (solo lectura, para poder asignar roles
        // existentes a sus usuarios). Antes de esta HU existían aquí POST/PUT-permissions/DELETE
        // restringidos a AdminCompanyPolicy — se eliminaron junto con
        // SetTenantRolePermissionsHandler e InsufficientPermissionsForDelegationException.

        // HU #10506 AC1/AC2 — PUT /users/{userId}/role — asigna un rol ADICIONAL (ya no
        // reemplaza los demás roles activos del usuario, a diferencia de HU #10164).
        group.MapPut("/users/{userId:guid}/role", async (
            Guid userId,
            [FromBody] AssignRoleRequest request,
            ClaimsPrincipal caller,
            AssignRoleHandler handler,
            FlitDbContext db,
            HttpContext httpContext,
            ILoggerFactory lf,
            CancellationToken cancellationToken) =>
        {
            var logger = lf.CreateLogger(nameof(SecurityEndpoints));
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            // Solo el perfil FLIT lleva el rol SuperAdmin, y solo un SuperAdmin lo reparte (en su
            // propio tenant interno). Para cualquier otro caller —AdminCompany, ot_admin— esto
            // sería escalada de privilegios: el rol es de TargetEntityType COMPANY, así que
            // AssignRoleHandler lo aceptaría sin más dentro de una compañía.
            var callerIsSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
            if (!callerIsSuperAdmin && await ContainsSuperAdminRoleAsync(db, [request.RoleId], cancellationToken))
                return SuperAdminRoleNotAssignable();

            // Roles vigentes ANTES del cambio, para que el rastro diga qué rol tenía y cuál
            // quedó — un "assign_role" a secas no le sirve a quien audita.
            var rolesBefore = await (
                from a in db.UserRoleAssignments.AsNoTracking()
                join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                // Sin filtrar por tenant: el SuperAdmin opera desde el tenant interno de FLIT, así
                // que filtrar por el suyo dejaría el rastro sin el "antes" en el caso más común.
                where a.UserId == userId && a.DeletedAt == null
                select r.Name
            ).ToListAsync(cancellationToken);

            try
            {
                await handler.HandleAsync(
                    new AssignRoleCommand(tenantId, userId, request.RoleId, callerId, callerIsSuperAdmin),
                    cancellationToken);

                var assignedRoleName = await db.Roles
                    .AsNoTracking()
                    .Where(r => r.Id == request.RoleId)
                    .Select(r => r.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                AdminAuditDetailContext.SetChange(
                    httpContext,
                    new { roles = rolesBefore },
                    new { rolAsignado = assignedRoleName ?? request.RoleId.ToString() });

                return Results.Ok();
            }
            catch (SelfRoleAssignmentException)
            {
                return Results.Json(
                    new ErrorResponse("SELF_ROLE_ASSIGNMENT", "No puedes cambiar tu propio rol."),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (UserOutOfScopeException)
            {
                return Results.Json(
                    new ErrorResponse("OUT_OF_SCOPE", "El usuario no pertenece al tenant."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (RoleForAssignmentNotFoundException)
            {
                return Results.Json(
                    new ErrorResponse("ROLE_NOT_FOUND", "Rol no encontrado o inactivo."),
                    statusCode: StatusCodes.Status404NotFound);
            }
            catch (RoleTargetEntityTypeMismatchException)
            {
                return Results.Json(
                    new ErrorResponse("ROLE_TARGET_ENTITY_TYPE_MISMATCH", "El rol no aplica al tipo de tenant destino."),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (RoleAlreadyAssignedException)
            {
                return Results.Json(
                    new ErrorResponse("ROLE_ALREADY_ASSIGNED", "El usuario ya tiene este rol asignado activamente."),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (Exception ex)
            {
#pragma warning disable CA1848
                logger.LogError(ex, "Error inesperado al asignar rol {RoleId} al usuario {UserId} en tenant {TenantId}",
                    request.RoleId, userId, tenantId);
#pragma warning restore CA1848
                return Results.Json(
                    new ErrorResponse("ASSIGN_ROLE_ERROR", ex.Message),
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }).RequireAuthorization(AdminAuthorization.UserAdminPolicy)
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.AssignRole, "user", "USER", "userId"));

        // HU #10621 — PATCH /users/{userId} — edita nombre y/o correo de un usuario del
        // alcance del caller. rowVersion es obligatorio (concurrencia optimista, AC4).
        group.MapPatch("/users/{userId:guid}", async (
            Guid userId,
            [FromBody] UpdateUserRequest request,
            ClaimsPrincipal caller,
            UpdateUserHandler handler,
            FlitDbContext db,
            HttpContext httpContext,
            ILoggerFactory lf,
            CancellationToken cancellationToken) =>
        {
            var logger = lf.CreateLogger(nameof(SecurityEndpoints));
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            // Multi-rol (HU #10506): FindFirstValue solo evalúa el primer claim "role" del JWT,
            // en orden no determinístico — se evalúan TODOS los claims de ese tipo (fix post-review #10504).
            var isSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            // Detalle para la auditoría: se lee el estado ANTES de editar para que el rastro
            // pueda responder "qué cambió" y no solo "hubo un update" (AdminAuditDetailContext).
            var before = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.DisplayName, u.Email })
                .FirstOrDefaultAsync(cancellationToken);

            try
            {
                await handler.HandleAsync(
                    new UpdateUserCommand(
                        tenantId, userId, request.DisplayName, request.Email, request.RowVersion, callerId, isSuperAdmin),
                    cancellationToken);

                if (before is not null)
                {
                    AdminAuditDetailContext.SetChange(
                        httpContext,
                        new { nombre = before.DisplayName, correo = before.Email },
                        new
                        {
                            nombre = request.DisplayName ?? before.DisplayName,
                            correo = request.Email ?? before.Email,
                        });
                }

                return Results.Ok();
            }
            catch (TargetUserNotFoundException)
            {
                return Results.Json(
                    new ErrorResponse("USER_NOT_FOUND", "El usuario no existe."),
                    statusCode: StatusCodes.Status404NotFound);
            }
            catch (UserOutOfScopeException)
            {
                return Results.Json(
                    new ErrorResponse("OUT_OF_SCOPE", "El usuario no pertenece al tenant."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (UserAlreadyExistsException)
            {
                return Results.Json(
                    new ErrorResponse("USER_ALREADY_EXISTS", UserEmailConflictMessages.EmailAlreadyInUse),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (UserEmailBelongsToDeletedAccountException)
            {
                // HU #11550 — mensaje visible unificado; el código de error se mantiene.
                return Results.Json(
                    new ErrorResponse("EMAIL_BELONGS_TO_DELETED_USER", UserEmailConflictMessages.EmailAlreadyInUse),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (UserProfileConcurrencyException ex)
            {
                return Results.Json(
                    new ErrorResponse("CONCURRENCY_CONFLICT", ex.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(
                    new ErrorResponse("VALIDATION_ERROR", ex.Message),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
#pragma warning disable CA1848
                logger.LogError(ex, "Error inesperado al editar el usuario {UserId} en tenant {TenantId}",
                    userId, tenantId);
#pragma warning restore CA1848
                return Results.Json(
                    new ErrorResponse("UPDATE_USER_ERROR", ex.Message),
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Users, AuditVocabulary.Operations.Update, "user", "USER", "userId"));

        // HU #10506 AC3 — DELETE /users/{userId}/roles/{roleId} — quita un rol puntual sin
        // afectar los demás roles activos del usuario (modelo aditivo).
        group.MapDelete("/users/{userId:guid}/roles/{roleId:guid}", async (
            Guid userId,
            Guid roleId,
            ClaimsPrincipal caller,
            RemoveRoleAssignmentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            var callerIsSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            try
            {
                await handler.HandleAsync(
                    userId, tenantId, roleId, callerId, cancellationToken, callerIsSuperAdmin);
                return Results.NoContent();
            }
            catch (RoleAssignmentNotFoundException)
            {
                return Results.Json(
                    new ErrorResponse("ROLE_ASSIGNMENT_NOT_FOUND", "El usuario no tiene ese rol asignado activamente."),
                    statusCode: StatusCodes.Status404NotFound);
            }
        }).RequireAuthorization(AdminAuthorization.UserAdminPolicy)
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Roles, AuditVocabulary.Operations.RevokeRole, "user", "USER", "userId"));

        // GET /users — lista usuarios activos + invitaciones pendientes
        // SuperAdmin ve todos los usuarios de todas las compañías (excluye su propio tenant interno)
        group.MapGet("/users", async (
            ClaimsPrincipal caller,
            FlitDbContext db,
            bool? onlyDeleted,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            // Multi-rol (HU #10506): FindFirstValue solo evalúa el primer claim "role" del JWT,
            // en orden no determinístico — se evalúan TODOS los claims de ese tipo (fix post-review #10504).
            var isSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            // HU #10624 AC3/AC4 — onlyDeleted=true: vista administrativa GLOBAL de usuarios
            // eliminados (cualquier tenant), EXCLUSIVA de SuperAdmin (único rol que puede
            // restaurar — ver POST /api/v1/superadmin/users/{userId}/restore). Sin este
            // parámetro el comportamiento no cambia (compatibilidad hacia atrás).
            if (onlyDeleted == true)
            {
                if (!isSuperAdmin)
                    return Results.Json(
                        new ErrorResponse("FORBIDDEN_SCOPE", "Solo SuperAdmin puede ver usuarios eliminados."),
                        statusCode: StatusCodes.Status403Forbidden);

                var deletedWithRole = await (
                    from a in db.UserRoleAssignments.AsNoTracking()
                    join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                    join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                    join t in db.Tenants.AsNoTracking() on a.TenantId equals t.Id
                    where a.DeletedAt == null && u.DeletedAt != null
                    // Más reciente primero: si quedaran datos multi-rol previos a la migración,
                    // el colapso de WithProfile se queda con el último rol asignado.
                    orderby a.AssignedAt descending
                    select new TenantUserDto(
                        u.Id.ToString(),
                        u.DisplayName,
                        u.Email,
                        r.Name,
                        r.Code,
                        a.RoleId,
                        u.Status == "active" ? "active" : "inactive",
                        null,
                        false,
                        t.Id.ToString(),
                        t.LegalName,
                        u.RowVersion,
                        u.DeletedAt,
                        db.TransitOfficeProfiles.Any(p => p.TenantId == t.Id)
                            ? TenantTypes.TransitOffice
                            : TenantTypes.Company)
                ).ToListAsync(cancellationToken);

                var deletedWithoutRole = await (
                    from u in db.Users.AsNoTracking()
                    join t in db.Tenants.AsNoTracking() on u.HomeTenantId equals t.Id
                    where u.DeletedAt != null
                          && !db.UserRoleAssignments.Any(a => a.UserId == u.Id && a.DeletedAt == null)
                    select new TenantUserDto(
                        u.Id.ToString(),
                        u.DisplayName,
                        u.Email,
                        null,
                        null,
                        null,
                        u.Status == "active" ? "active" : "inactive",
                        null,
                        false,
                        t.Id.ToString(),
                        t.LegalName,
                        u.RowVersion,
                        u.DeletedAt,
                        db.TransitOfficeProfiles.Any(p => p.TenantId == t.Id)
                            ? TenantTypes.TransitOffice
                            : TenantTypes.Company)
                ).ToListAsync(cancellationToken);

                return Results.Ok(WithProfile(deletedWithRole.Concat(deletedWithoutRole)
                    .OrderByDescending(x => x.DeletedAt)));
            }

            var now = DateTimeOffset.UtcNow;

            if (isSuperAdmin)
            {
                // SuperAdmin ve todos los usuarios de todos los tenants (excepto el propio DEMO)
                var allUsers = await (
                    from a in db.UserRoleAssignments.AsNoTracking()
                    join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                    join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                    join t in db.Tenants.AsNoTracking() on a.TenantId equals t.Id
                    where a.TenantId != callerTenantId && a.DeletedAt == null && u.DeletedAt == null
                    orderby a.AssignedAt descending
                    select new TenantUserDto(
                        u.Id.ToString(),
                        u.DisplayName,
                        u.Email,
                        r.Name,
                        r.Code,
                        a.RoleId,
                        u.Status == "active" ? "active" : "inactive",
                        null,
                        false,
                        t.Id.ToString(),
                        t.LegalName,
                        u.RowVersion,
                        null,
                        db.TransitOfficeProfiles.Any(p => p.TenantId == t.Id)
                            ? TenantTypes.TransitOffice
                            : TenantTypes.Company)
                ).ToListAsync(cancellationToken);

                // SuperAdmin also sees users that belong to other tenants but have no role assignment
                var allUsersWithoutRole = await (
                    from u in db.Users.AsNoTracking()
                    join t in db.Tenants.AsNoTracking() on u.HomeTenantId equals t.Id
                    where u.HomeTenantId != callerTenantId
                          && u.DeletedAt == null
                          && !db.UserRoleAssignments.Any(a => a.UserId == u.Id && a.DeletedAt == null)
                    select new TenantUserDto(
                        u.Id.ToString(),
                        u.DisplayName,
                        u.Email,
                        null,
                        null,
                        null,
                        u.Status == "active" ? "active" : "inactive",
                        null,
                        false,
                        t.Id.ToString(),
                        t.LegalName,
                        u.RowVersion,
                        null,
                        db.TransitOfficeProfiles.Any(p => p.TenantId == t.Id)
                            ? TenantTypes.TransitOffice
                            : TenantTypes.Company)
                ).ToListAsync(cancellationToken);

                var allPending = await (
                    from i in db.UserInvitations.AsNoTracking()
                    join t in db.Tenants.AsNoTracking() on i.TenantId equals t.Id
                    where i.TenantId != callerTenantId && i.Status == "pending"
                    orderby i.CreatedAt descending
                    select new TenantUserDto(
                        i.Id.ToString(),
                        i.FullName,
                        i.Email,
                        // El rol de una invitación pendiente ya está decidido: mostrarlo evita
                        // que la columna Perfil / Rol quede en "—" hasta que el usuario active.
                        db.Roles.Where(r => r.Id == i.RoleId).Select(r => r.Name).FirstOrDefault(),
                        db.Roles.Where(r => r.Id == i.RoleId).Select(r => r.Code).FirstOrDefault(),
                        i.RoleId,
                        "pending",
                        i.CreatedAt,
                        false,
                        t.Id.ToString(),
                        t.LegalName,
                        0L,
                        null,
                        db.TransitOfficeProfiles.Any(p => p.TenantId == t.Id)
                            ? TenantTypes.TransitOffice
                            : TenantTypes.Company)
                ).ToListAsync(cancellationToken);

                // Perfil FLIT: los usuarios del propio tenant interno quedaban fuera del listado
                // (`a.TenantId != callerTenantId`), así que un SuperAdmin recién creado era
                // invisible para quien lo creó. Se incluyen SOLO los que tienen el rol de sistema
                // SuperAdmin — el resto del tenant interno sigue sin exponerse.
                var flitUsers = await (
                    from a in db.UserRoleAssignments.AsNoTracking()
                    join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                    join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                    join t in db.Tenants.AsNoTracking() on a.TenantId equals t.Id
                    where a.TenantId == callerTenantId
                          && a.DeletedAt == null
                          && u.DeletedAt == null
                          && r.Code == AdminAuthorization.SuperAdminRole
                    select new TenantUserDto(
                        u.Id.ToString(),
                        u.DisplayName,
                        u.Email,
                        r.Name,
                        r.Code,
                        a.RoleId,
                        u.Status == "active" ? "active" : "inactive",
                        null,
                        false,
                        t.Id.ToString(),
                        t.LegalName,
                        u.RowVersion,
                        null,
                        TenantTypes.Company)
                ).ToListAsync(cancellationToken);

                // Invitaciones pendientes al tenant interno: por construcción solo pueden ser de
                // perfil FLIT (invitar a otro tenant propio con rol no-SuperAdmin está bloqueado).
                var flitPending = await (
                    from i in db.UserInvitations.AsNoTracking()
                    join t in db.Tenants.AsNoTracking() on i.TenantId equals t.Id
                    where i.TenantId == callerTenantId && i.Status == "pending"
                    orderby i.CreatedAt descending
                    select new TenantUserDto(
                        i.Id.ToString(),
                        i.FullName,
                        i.Email,
                        db.Roles.Where(r => r.Id == i.RoleId).Select(r => r.Name).FirstOrDefault(),
                        db.Roles.Where(r => r.Id == i.RoleId).Select(r => r.Code).FirstOrDefault(),
                        i.RoleId,
                        "pending",
                        i.CreatedAt,
                        false,
                        t.Id.ToString(),
                        t.LegalName,
                        0L,
                        null,
                        TenantTypes.Company)
                ).ToListAsync(cancellationToken);

                return Results.Ok(WithProfile(allUsers
                    .Concat(allUsersWithoutRole)
                    .Concat(allPending)
                    .Concat(flitUsers)
                    .Concat(flitPending)));
            }

            // AdminCompany: solo ve su tenant
            var tenantId = callerTenantId;

            // El perfil (Gestor vs OT) de todo este listado lo fija el tenant del caller: un
            // AdminCompany/ot_admin solo ve usuarios de su propio tenant.
            var callerTenantType = await db.TransitOfficeProfiles
                .AsNoTracking()
                .AnyAsync(p => p.TenantId == tenantId, cancellationToken)
                ? TenantTypes.TransitOffice
                : TenantTypes.Company;

            var activeUsers = await (
                from a in db.UserRoleAssignments.AsNoTracking()
                join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                where a.TenantId == tenantId && a.DeletedAt == null && u.DeletedAt == null
                orderby a.AssignedAt descending
                select new TenantUserDto(
                    u.Id.ToString(),
                    u.DisplayName,
                    u.Email,
                    r.Name,
                    r.Code,
                    a.RoleId,
                    u.Status == "active" ? "active" : "inactive",
                    null,
                    db.UserTempSuspensions.Any(s => s.UserId == u.Id && s.TenantId == tenantId
                        && s.DeletedAt == null && s.StartsAt <= now && (s.EndsAt == null || s.EndsAt >= now)),
                    null,
                    null,
                    u.RowVersion,
                    null,
                    callerTenantType)
            ).ToListAsync(cancellationToken);

            var usersWithoutRole = await (
                from u in db.Users.AsNoTracking()
                where u.HomeTenantId == tenantId
                      && u.DeletedAt == null
                      && !db.UserRoleAssignments.Any(a => a.UserId == u.Id && a.TenantId == tenantId && a.DeletedAt == null)
                select new TenantUserDto(
                    u.Id.ToString(),
                    u.DisplayName,
                    u.Email,
                    null,
                    null,
                    null,
                    u.Status == "active" ? "active" : "inactive",
                    null,
                    db.UserTempSuspensions.Any(s => s.UserId == u.Id && s.TenantId == tenantId
                        && s.DeletedAt == null && s.StartsAt <= now && (s.EndsAt == null || s.EndsAt >= now)),
                    null,
                    null,
                    u.RowVersion,
                    null,
                    callerTenantType)
            ).ToListAsync(cancellationToken);

            var pending = await db.UserInvitations
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Status == "pending")
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new TenantUserDto(
                    x.Id.ToString(),
                    x.FullName,
                    x.Email,
                    db.Roles.Where(r => r.Id == x.RoleId).Select(r => r.Name).FirstOrDefault(),
                    db.Roles.Where(r => r.Id == x.RoleId).Select(r => r.Code).FirstOrDefault(),
                    x.RoleId,
                    "pending",
                    x.CreatedAt,
                    false,
                    null,
                    null,
                    0L,
                    null,
                    callerTenantType))
                .ToListAsync(cancellationToken);

            return Results.Ok(WithProfile(activeUsers.Concat(usersWithoutRole).Concat(pending)));
        });

        // POST /security/users/{userId}/suspend — suspende (temporal, con EndsAt) o desactiva
        // indefinidamente (sin EndsAt). AdminCompany solo su tenant; SuperAdmin cualquiera
        // (handler AC3). Antes era SuperAdmin-only; se reabre a AdminCompanyPolicy (auth-parity).
        group.MapPost("/users/{userId:guid}/suspend", async (
            Guid userId,
            [FromBody] SuspendUserRequest request,
            ClaimsPrincipal caller,
            SuspendUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            // Multi-rol (HU #10506): FindFirstValue solo evalúa el primer claim "role" del JWT,
            // en orden no determinístico — se evalúan TODOS los claims de ese tipo (fix post-review #10504).
            var callerIsSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            try
            {
                var suspensionId = await handler.HandleAsync(
                    new SuspendUserCommand(callerTenantId, userId, request.Reason, request.EndsAt, callerId, callerIsSuperAdmin),
                    cancellationToken);

                return Results.Created($"/api/v1/security/users/{userId}/suspend", new { id = suspensionId });
            }
            catch (TargetUserNotFoundException)
            {
                return Results.NotFound(new ErrorResponse("USER_NOT_FOUND", "El usuario no existe en este tenant."));
            }
            catch (UserOutOfScopeException)
            {
                return Results.Json(
                    new ErrorResponse("FORBIDDEN_SCOPE", "No tiene ámbito sobre este usuario."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (SelfSuspensionException)
            {
                return Results.BadRequest(new ErrorResponse("SELF_SUSPEND", "No puedes suspenderte a ti mismo."));
            }
            catch (LastActiveAdminException)
            {
                return Results.Conflict(new ErrorResponse(
                    "LAST_ACTIVE_ADMIN", "No es posible suspender/desactivar al último administrador activo."));
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Users, AuditVocabulary.Operations.Suspend, "user", "USER", "userId"));

        // DELETE /security/users/{userId}/suspend — levanta la suspensión/desactivación activa.
        // Misma policy que suspender (AdminCompany su tenant / SuperAdmin).
        group.MapDelete("/users/{userId:guid}/suspend", async (
            Guid userId,
            ClaimsPrincipal caller,
            UnsuspendUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            var callerIsSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            try
            {
                await handler.HandleAsync(
                    new UnsuspendUserCommand(callerTenantId, userId, callerId, callerIsSuperAdmin),
                    cancellationToken);

                return Results.NoContent();
            }
            catch (TargetUserNotFoundException)
            {
                return Results.NotFound(new ErrorResponse("USER_NOT_FOUND", "El usuario no existe en este tenant."));
            }
            catch (UserOutOfScopeException)
            {
                return Results.Json(
                    new ErrorResponse("FORBIDDEN_SCOPE", "No tiene ámbito sobre este usuario."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (NoActiveSuspensionException)
            {
                return Results.NotFound(new ErrorResponse("NO_ACTIVE_SUSPENSION", "El usuario no tiene una suspensión activa."));
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Users, AuditVocabulary.Operations.Unsuspend, "user", "USER", "userId"));

        // DELETE /security/users/{userId} — elimina (soft-delete reversible) a un usuario.
        // AdminCompany solo su tenant; SuperAdmin cualquiera. Restaurar sigue EXCLUSIVO de
        // SuperAdmin — ver POST /api/v1/superadmin/users/{userId}/restore. rowVersion obligatorio.
        group.MapDelete("/users/{userId:guid}", async (
            Guid userId,
            [FromBody] DeleteUserRequest request,
            ClaimsPrincipal caller,
            DeleteUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            // Multi-rol (HU #10506): FindFirstValue solo evalúa el primer claim "role" del JWT,
            // en orden no determinístico — se evalúan TODOS los claims de ese tipo.
            var callerIsSuperAdmin = caller.Claims.Any(c =>
                c.Type == AdminAuthorization.RoleClaimType
                && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

            try
            {
                await handler.HandleAsync(
                    new DeleteUserCommand(callerTenantId, userId, request.RowVersion, callerId, callerIsSuperAdmin),
                    cancellationToken);

                return Results.NoContent();
            }
            catch (TargetUserNotFoundException)
            {
                return Results.NotFound(new ErrorResponse("USER_NOT_FOUND", "El usuario no existe en este tenant."));
            }
            catch (UserOutOfScopeException)
            {
                return Results.Json(
                    new ErrorResponse("FORBIDDEN_SCOPE", "No tiene ámbito sobre este usuario."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (SelfDeletionException)
            {
                return Results.BadRequest(new ErrorResponse("SELF_DELETE", "No puedes eliminarte a ti mismo."));
            }
            catch (LastActiveAdminException)
            {
                return Results.Conflict(new ErrorResponse(
                    "LAST_ACTIVE_ADMIN", "No es posible eliminar al último administrador activo."));
            }
            catch (UserProfileConcurrencyException ex)
            {
                return Results.Json(
                    new ErrorResponse("CONCURRENCY_CONFLICT", ex.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .AddEndpointFilter(new AdminAuditFilter(
              AuditVocabulary.Modules.Users, AuditVocabulary.Operations.DeleteUser, "user", "USER", "userId"));

        return app;
    }

    private sealed record AssignRoleRequest(Guid RoleId);

    // HU #10621 — DisplayName/Email opcionales ("no tocar ese campo"); RowVersion obligatorio
    // (concurrencia optimista, AC4 — el valor que el frontend leyó de TenantUserDto.RowVersion).
    private sealed record UpdateUserRequest(string? DisplayName, string? Email, long RowVersion);

    // HU #10619 AC1: EndsAt nulo = desactivación indefinida (sin fecha de fin).
    private sealed record SuspendUserRequest(string Reason, DateTimeOffset? EndsAt);

    // HU #10623 — RowVersion obligatorio (concurrencia optimista, igual que UpdateUserRequest —
    // el valor leído de TenantUserDto.RowVersion).
    private sealed record DeleteUserRequest(long RowVersion);

    private sealed record CreateInvitationRequest(string Email, string? FullName, Guid[]? RoleIds, Guid? TargetTenantId);

    private sealed record InvitationCreatedResponse(Guid InvitationId, string Email, bool EmailSent);

    // HU #10624 AC3 — DeletedAt opcional (default null): usado por la vista "Eliminados" de
    // SuperAdmin (?onlyDeleted=true); el listado normal no lo popula (siempre null).
    //
    // TenantType/Profile: el perfil funcional (contexto-perfiles.md) lo resuelve el SERVIDOR, no
    // el frontend. Antes la UI lo infería del roleCode y cualquier rol personalizado de un tenant
    // OT terminaba etiquetado como Gestor. TenantType se proyecta en SQL (presencia de
    // TransitOfficeProfile) y Profile se deriva de (roleCode, tenantType) con ResolveProfile.
    private sealed record TenantUserDto(string Id, string FullName, string Email, string? Role, string? RoleCode, Guid? RoleId, string Status, DateTimeOffset? CreatedAt, bool IsSuspended, string? TenantId, string? TenantName, long RowVersion, DateTimeOffset? DeletedAt = null, string? TenantType = null, string? Profile = null);

    /// <summary>
    /// Perfil funcional FLIT — <c>FLIT</c> | <c>OT</c> | <c>GESTOR</c> (ver context/contexto-perfiles.md).
    /// El rol de sistema SuperAdmin es transversal a la plataforma y manda sobre el tipo de tenant;
    /// en el resto de casos decide el tenant al que pertenece la asignación.
    /// </summary>
    private static string ResolveProfile(string? roleCode, string? tenantType) =>
        string.Equals(roleCode, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase)
            ? UserProfiles.Flit
            : string.Equals(tenantType, TenantTypes.TransitOffice, StringComparison.OrdinalIgnoreCase)
                ? UserProfiles.TransitOffice
                : UserProfiles.Manager;

    /// <summary>
    /// Resuelve el perfil de cada fila y deja UNA fila por (usuario, tenant).
    ///
    /// El listado se arma desde <c>user_role_assignments</c>, así que un usuario con dos
    /// asignaciones activas salía duplicado. Con el rol único eso ya no debería pasar —lo
    /// garantiza <c>uq_ura_active_user_tenant</c>—, pero el colapso se queda como red: los datos
    /// anteriores a la migración siguen existiendo en entornos que aún no la corrieron. La clave
    /// incluye el tenant porque un usuario sí puede pertenecer a más de uno, con un rol en cada.
    /// </summary>
    private static List<TenantUserDto> WithProfile(IEnumerable<TenantUserDto> rows) =>
        rows.Select(r => r with { Profile = ResolveProfile(r.RoleCode, r.TenantType) })
            .GroupBy(r => (r.Id, r.TenantId))
            .Select(g => g.First())
            .ToList();

    /// <summary>
    /// ¿Alguno de estos roles es el SuperAdmin? Se consulta por Code y no por un id cacheado
    /// porque <c>security.roles</c> es un catálogo global y el id no está fijado en configuración.
    /// </summary>
    private static async Task<bool> ContainsSuperAdminRoleAsync(
        FlitDbContext db, IReadOnlyList<Guid> roleIds, CancellationToken ct) =>
        roleIds.Count > 0
        && await db.Roles
            .AsNoTracking()
            .AnyAsync(r => roleIds.Contains(r.Id) && r.Code == AdminAuthorization.SuperAdminRole, ct);

    private static IResult SuperAdminRoleNotAssignable() =>
        Results.Json(
            new ErrorResponse(
                "SUPERADMIN_ROLE_NOT_ASSIGNABLE",
                "El rol Super Administrador pertenece al perfil FLIT y no puede asignarse desde una compañía ni desde un organismo de tránsito."),
            statusCode: StatusCodes.Status403Forbidden);

    private sealed record ErrorResponse(string Code, string Message);

    private sealed record ResendCooldownResponse(string Code, string Message, int RetryAfterSeconds);
}
