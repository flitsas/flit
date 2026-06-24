using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Modules;
using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Application.UserRoles;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.Roles;
using Flit.Modules.Security.Domain.UserRoles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IRoleRepository = Flit.Modules.Security.Domain.Roles.IRoleRepository;
using AuthRoleNotFoundException = Flit.Modules.Security.Domain.Auth.RoleNotFoundException;
using RolesRoleNotFoundException = Flit.Modules.Security.Domain.Roles.RoleNotFoundException;

namespace Flit.Api.Endpoints;

public static class SecurityEndpoints
{
    private static readonly string[] ReservedRoleCodes = ["SuperAdmin", "AdminCompany"];
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/security").RequireAuthorization();

        // HU #10175 — crear invitación por email (rol opcional)
        // Fase 3 fix: SuperAdmin puede especificar TargetTenantId para invitar a otro tenant
        group.MapPost("/invitations", async (
            [FromBody] CreateInvitationRequest request,
            ClaimsPrincipal caller,
            CreateInvitationHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var callerTenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var invitedBy))
                return Results.Unauthorized();

            var roleCode = caller.FindFirstValue(AdminAuthorization.RoleClaimType) ?? string.Empty;
            var isSuperAdmin = roleCode == AdminAuthorization.SuperAdminRole;

            // SuperAdmin puede invitar a otro tenant; AdminCompany siempre usa el propio
            var targetTenantId = isSuperAdmin && request.TargetTenantId.HasValue
                ? request.TargetTenantId.Value
                : callerTenantId;

            Guid? roleId = Guid.TryParse(request.RoleId, out var parsed) ? parsed : null;

            try
            {
                var result = await handler.HandleAsync(
                    new CreateInvitationCommand(targetTenantId, request.Email, request.FullName ?? string.Empty, roleId, invitedBy),
                    cancellationToken);

                return Results.Created(
                    $"/api/v1/security/invitations/{result.InvitationId}",
                    new InvitationCreatedResponse(result.InvitationId, result.Email, result.EmailSent));
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
                    new ErrorResponse("INVITATION_ALREADY_PENDING", "Ya existe una invitación pendiente para este correo."),
                    statusCode: StatusCodes.Status409Conflict);
            }
        });

        // GET /security/modules — módulos y acciones accesibles al caller según sus permisos JWT
        group.MapGet("/modules", async (
            ClaimsPrincipal caller,
            ListAccessibleModulesHandler handler,
            CancellationToken ct) =>
        {
            var roleCode = caller.FindFirstValue(AdminAuthorization.RoleClaimType) ?? string.Empty;
            var isSuperAdmin = roleCode == AdminAuthorization.SuperAdminRole;
            var permissions = caller.FindAll("permissions").Select(c => c.Value).ToList();

            var modules = await handler.HandleAsync(permissions, isSuperAdmin, ct);
            return Results.Ok(modules);
        }).WithName("ListAccessibleModules");

        // AC5 — GET /roles — lista roles activos del tenant del caller (HU #10164)
        group.MapGet("/roles", async (
            ClaimsPrincipal caller,
            IRoleRepository roleRepo,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var roles = await roleRepo.ListByTenantAsync(tenantId, cancellationToken);
            return Results.Ok(roles);
        });

        // POST /security/roles — AdminCompany crea rol en su tenant (Fase 2)
        group.MapPost("/roles", async (
            [FromBody] CreateRoleRequest request,
            ClaimsPrincipal caller,
            CreateRoleHandler handler,
            CancellationToken ct) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            if (ReservedRoleCodes.Contains(request.Code, StringComparer.OrdinalIgnoreCase))
                return Results.Json(
                    new ErrorResponse("RESERVED_ROLE_CODE", "El código de rol está reservado por el sistema."),
                    statusCode: StatusCodes.Status400BadRequest);

            try
            {
                var id = await handler.HandleAsync(
                    new CreateRoleCommand(tenantId, request.Code, request.Name, request.Description),
                    ct);
                return Results.Created($"/api/v1/security/roles/{id}", new { id });
            }
            catch (RoleCodeDuplicateException)
            {
                return Results.Conflict(new ErrorResponse("ROLE_CODE_DUPLICATE", "Ya existe un rol con ese código en tu empresa."));
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("CreateTenantRole");

        // PUT /security/roles/{id}/permissions — AdminCompany asigna permisos (subset del propio rol)
        group.MapPut("/roles/{id:guid}/permissions", async (
            Guid id,
            [FromBody] SetRolePermissionsRequest request,
            ClaimsPrincipal caller,
            SetTenantRolePermissionsHandler handler,
            CancellationToken ct) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var callerPermissions = caller.FindAll("permissions").Select(c => c.Value).ToList();

            try
            {
                var detail = await handler.HandleAsync(
                    new SetTenantRolePermissionsCommand(id, tenantId, callerPermissions, request.PermissionIds),
                    ct);
                return Results.Ok(detail);
            }
            catch (RolesRoleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InsufficientPermissionsForDelegationException)
            {
                return Results.Json(
                    new ErrorResponse("INSUFFICIENT_PERMISSIONS", "No puede asignar permisos que no posee."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("SetTenantRolePermissions");

        // DELETE /security/roles/{id} — AdminCompany elimina rol no-sistema de su tenant
        group.MapDelete("/roles/{id:guid}", async (
            Guid id,
            ClaimsPrincipal caller,
            IRoleRepository roleRepository,
            DeleteRoleHandler handler,
            CancellationToken ct) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var role = await roleRepository.GetByIdAsync(id, ct);
            if (role is null || role.TenantId != tenantId)
                return Results.NotFound();

            try
            {
                await handler.HandleAsync(id, ct);
                return Results.NoContent();
            }
            catch (RolesRoleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RoleSystemLockedException)
            {
                return Results.Conflict(new ErrorResponse("ROLE_SYSTEM_LOCKED", "Los roles de sistema no pueden eliminarse."));
            }
            catch (RoleHasActiveUsersException)
            {
                return Results.Conflict(new ErrorResponse("ROLE_HAS_ACTIVE_USERS", "El rol tiene usuarios activos asignados."));
            }
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
          .WithName("DeleteTenantRole");

        // AC1/AC2 — PUT /users/{userId}/role — asigna o reemplaza rol (HU #10164)
        group.MapPut("/users/{userId:guid}/role", async (
            Guid userId,
            [FromBody] AssignRoleRequest request,
            ClaimsPrincipal caller,
            AssignRoleHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            try
            {
                await handler.HandleAsync(
                    new AssignRoleCommand(tenantId, userId, request.RoleId, callerId),
                    cancellationToken);
                return Results.Ok();
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
        });

        // GET /users — lista usuarios activos + invitaciones pendientes del tenant
        group.MapGet("/users", async (
            ClaimsPrincipal caller,
            FlitDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;

            var activeUsers = await (
                from a in db.UserRoleAssignments.AsNoTracking()
                join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                where a.TenantId == tenantId && a.DeletedAt == null && u.DeletedAt == null
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
                        && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now))
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
                    "inactive",
                    null,
                    db.UserTempSuspensions.Any(s => s.UserId == u.Id && s.TenantId == tenantId
                        && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now))
            ).ToListAsync(cancellationToken);

            var pending = await db.UserInvitations
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Status == "pending")
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new TenantUserDto(
                    x.Id.ToString(),
                    x.FullName,
                    x.Email,
                    null,
                    null,
                    null,
                    "pending",
                    x.CreatedAt,
                    false))
                .ToListAsync(cancellationToken);

            var result = activeUsers
                .Concat(usersWithoutRole)
                .Concat(pending)
                .ToList();

            return Results.Ok(result);
        });

        // POST /security/users/{userId}/suspend — AdminCompany bloquea usuario temporalmente
        group.MapPost("/users/{userId:guid}/suspend", async (
            Guid userId,
            [FromBody] SuspendUserRequest request,
            ClaimsPrincipal caller,
            FlitDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
            _ = Guid.TryParse(subClaim, out var callerId);

            if (callerId == userId)
                return Results.BadRequest(new ErrorResponse("SELF_SUSPEND", "No puedes suspenderte a ti mismo."));

            var userExistsInTenant = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.DeletedAt == null
                    && (db.UserRoleAssignments.Any(a => a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null)
                        || u.HomeTenantId == tenantId),
                    cancellationToken);

            if (!userExistsInTenant)
                return Results.NotFound(new ErrorResponse("USER_NOT_FOUND", "El usuario no existe en este tenant."));

            var targetIsSuperAdmin = await db.UserRoleAssignments.AsNoTracking()
                .AnyAsync(a => a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null
                    && db.Roles.Any(r => r.Id == a.RoleId && r.Code == AdminAuthorization.SuperAdminRole),
                    cancellationToken);

            if (targetIsSuperAdmin)
                return Results.Conflict(new ErrorResponse("CANNOT_SUSPEND_ADMIN", "No es posible suspender a un SuperAdmin."));

            var now = DateTimeOffset.UtcNow;

            var existing = await db.UserTempSuspensions
                .Where(s => s.UserId == userId && s.TenantId == tenantId
                         && s.DeletedAt == null && s.EndsAt >= now)
                .ToListAsync(cancellationToken);

            foreach (var s in existing)
            {
                s.DeletedAt = now;
                s.DeletedBy = callerId == Guid.Empty ? null : callerId;
            }

            var suspension = new UserTempSuspension
            {
                TenantId = tenantId,
                UserId = userId,
                StartsAt = now,
                EndsAt = request.EndsAt.ToUniversalTime(),
                Reason = request.Reason,
                CreatedAt = now,
                CreatedBy = callerId == Guid.Empty ? null : callerId,
            };

            db.UserTempSuspensions.Add(suspension);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/v1/security/users/{userId}/suspend", new { id = suspension.Id });
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy);

        // DELETE /security/users/{userId}/suspend — levanta la suspensión activa
        group.MapDelete("/users/{userId:guid}/suspend", async (
            Guid userId,
            ClaimsPrincipal caller,
            FlitDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
            _ = Guid.TryParse(subClaim, out var callerId);

            var now = DateTimeOffset.UtcNow;
            var active = await db.UserTempSuspensions
                .Where(s => s.UserId == userId && s.TenantId == tenantId
                         && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now)
                .ToListAsync(cancellationToken);

            if (active.Count == 0)
                return Results.NotFound(new ErrorResponse("NO_ACTIVE_SUSPENSION", "El usuario no tiene una suspensión activa."));

            foreach (var s in active)
            {
                s.DeletedAt = now;
                s.DeletedBy = callerId == Guid.Empty ? null : callerId;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(AdminAuthorization.AdminCompanyPolicy);

        return app;
    }

    private sealed record AssignRoleRequest(Guid RoleId);

    private sealed record CreateRoleRequest(string Code, string Name, string? Description);

    private sealed record SetRolePermissionsRequest(List<Guid> PermissionIds);

    private sealed record SuspendUserRequest(string Reason, DateTimeOffset EndsAt);

    private sealed record CreateInvitationRequest(string Email, string? FullName, string? RoleId, Guid? TargetTenantId);

    private sealed record InvitationCreatedResponse(Guid InvitationId, string Email, bool EmailSent);

    private sealed record TenantUserDto(string Id, string FullName, string Email, string? Role, string? RoleCode, Guid? RoleId, string Status, DateTimeOffset? CreatedAt, bool IsSuspended);

    private sealed record ErrorResponse(string Code, string Message);
}
