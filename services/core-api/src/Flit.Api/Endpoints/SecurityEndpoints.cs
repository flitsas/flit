using System.Security.Claims;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/security").RequireAuthorization();

        // HU #10175 — crear invitación por email (rol opcional)
        group.MapPost("/invitations", async (
            [FromBody] CreateInvitationRequest request,
            ClaimsPrincipal caller,
            CreateInvitationHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var invitedBy))
                return Results.Unauthorized();

            Guid? roleId = Guid.TryParse(request.RoleId, out var parsed) ? parsed : null;

            try
            {
                var result = await handler.HandleAsync(
                    new CreateInvitationCommand(tenantId, request.Email, request.FullName ?? string.Empty, roleId, invitedBy),
                    cancellationToken);

                return Results.Created(
                    $"/api/v1/security/invitations/{result.InvitationId}",
                    new InvitationCreatedResponse(result.InvitationId, result.Email, result.EmailSent));
            }
            catch (RoleNotFoundException)
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

        // GET /users — lista usuarios activos + invitaciones pendientes del tenant
        group.MapGet("/users", async (
            ClaimsPrincipal caller,
            FlitDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.Unauthorized();

            // Active users via role assignments
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
                    u.Status == "active" ? "active" : "inactive",
                    null)
            ).ToListAsync(cancellationToken);

            // Also include users with no role but belonging to this tenant
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
                    "inactive",
                    null)
            ).ToListAsync(cancellationToken);

            // Pending invitations (not yet activated)
            var pending = await db.UserInvitations
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Status == "pending")
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new TenantUserDto(
                    x.Id.ToString(),
                    x.FullName,
                    x.Email,
                    null,
                    "pending",
                    x.CreatedAt))
                .ToListAsync(cancellationToken);

            var result = activeUsers
                .Concat(usersWithoutRole)
                .Concat(pending)
                .ToList();

            return Results.Ok(result);
        });

        return app;
    }

    private sealed record CreateInvitationRequest(string Email, string? FullName, string? RoleId);

    private sealed record InvitationCreatedResponse(Guid InvitationId, string Email, bool EmailSent);

    private sealed record TenantUserDto(string Id, string FullName, string Email, string? Role, string Status, DateTimeOffset? CreatedAt);

    private sealed record ErrorResponse(string Code, string Message);
}
