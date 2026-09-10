using System.Globalization;
using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Invitations;
using Flit.Admin.Domain.Companies;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using AuthRoleNotFoundException = Flit.Modules.Security.Domain.Auth.RoleNotFoundException;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Application.Auth.CancelInvitation;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.ResendInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #12354 — invitaciones y gestión de usuarios de clientes hijos por la cabeza de grupo.
/// No modifica <c>POST /security/invitations</c>.
/// </summary>
public static class AdminCompanyChildrenInvitationsEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompanyChildrenInvitationsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{headTenantId:guid}/children/{childTenantId:guid}")
            .RequireAuthorization(AdminAuthorization.GroupHeadCompanyPolicy)
            .WithTags("Admin · Usuarios de hijos");

        group.MapPost("/invitations", CreateInvitationAsync)
            .WithName("AdminCompanyChildCreateInvitation")
            .AddEndpointFilter(new AdminAuditFilter(
                AuditVocabulary.Modules.Users,
                AuditVocabulary.Operations.Invite,
                "invitation",
                "INVITATION"));

        group.MapPost("/invitations/{invitationId:guid}/resend", ResendInvitationAsync)
            .WithName("AdminCompanyChildResendInvitation");

        group.MapGet("/users", ListUsersAsync)
            .WithName("AdminCompanyChildListUsers");

        group.MapDelete("/users/{userId:guid}", DeactivateUserAsync)
            .WithName("AdminCompanyChildDeactivateUser")
            .AddEndpointFilter(new AdminAuditFilter(
                AuditVocabulary.Modules.Users,
                AuditVocabulary.Operations.Deactivate,
                "user",
                "USER",
                "userId"));

        return app;
    }

    private static async Task<IResult> CreateInvitationAsync(
        Guid headTenantId,
        Guid childTenantId,
        ChildInvitationRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] IGroupHeadInvitationRolePolicy rolePolicy,
        [FromServices] CreateInvitationHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        if (!RequestTenantResolver.TryResolveTenantId(user, out _))
        {
            return Results.Unauthorized();
        }

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var invitedBy))
        {
            return Results.Unauthorized();
        }

        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        if (roleIds.Count == 0)
        {
            return Results.Json(
                new { code = "NO_ROLES_SELECTED", message = "Debes seleccionar al menos un rol." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        foreach (var roleId in roleIds)
        {
            if (!await rolePolicy.IsAllowedRoleIdAsync(roleId, cancellationToken).ConfigureAwait(false))
            {
                return Results.Json(
                    new { code = "ROLE_NOT_ALLOWED", message = "El rol seleccionado no está permitido para invitaciones a clientes hijos." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        try
        {
            var result = await handler.HandleAsync(
                new CreateInvitationCommand(
                    childTenantId,
                    request.Email ?? string.Empty,
                    request.FullName ?? string.Empty,
                    roleIds,
                    invitedBy),
                cancellationToken).ConfigureAwait(false);

            return Results.Created(
                $"/api/v1/admin/companies/{headTenantId}/children/{childTenantId}/invitations/{result.InvitationId}",
                new { invitationId = result.InvitationId, email = result.Email, emailSent = result.EmailSent });
        }
        catch (NoRolesSelectedException)
        {
            return Results.Json(
                new { code = "NO_ROLES_SELECTED", message = "Debes seleccionar al menos un rol." },
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvitationAlreadyPendingException)
        {
            return Results.Json(
                new { code = UserEmailConflictMessages.EmailAlreadyInUseCode, message = UserEmailConflictMessages.EmailAlreadyInUse },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (UserAlreadyExistsException)
        {
            return Results.Json(
                new { code = UserEmailConflictMessages.EmailAlreadyInUseCode, message = UserEmailConflictMessages.EmailAlreadyInUse },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (UserEmailBelongsToDeletedAccountException)
        {
            return Results.Json(
                new { code = UserEmailConflictMessages.EmailAlreadyInUseCode, message = UserEmailConflictMessages.EmailAlreadyInUse },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (AuthRoleNotFoundException)
        {
            return Results.Json(
                new { code = "ROLE_NOT_FOUND", message = "El rol especificado no existe." },
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> ResendInvitationAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid invitationId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ResendInvitationHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var resentBy))
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await handler.HandleAsync(
                new ResendInvitationCommand(invitationId, childTenantId, resentBy),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { invitationId = result.InvitationId, email = result.Email, emailSent = result.EmailSent });
        }
        catch (InvitationNotFoundException)
        {
            return Results.Json(
                new { code = "INVITATION_NOT_FOUND", message = "La invitación no existe o no pertenece al cliente hijo." },
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (ResendCooldownActiveException ex)
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.TotalSeconds));
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Results.Json(
                new { code = "RESEND_COOLDOWN_ACTIVE", retryAfterSeconds },
                statusCode: StatusCodes.Status429TooManyRequests);
        }
    }

    private static async Task<IResult> ListUsersAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] FlitDbContext db,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var users = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join u in db.Users.AsNoTracking() on a.UserId equals u.Id
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.TenantId == childTenantId && a.DeletedAt == null && u.DeletedAt == null
            orderby u.DisplayName
            select new
            {
                userId = u.Id,
                u.DisplayName,
                u.Email,
                roleCode = r.Code,
                roleId = r.Id,
                status = u.Status,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { items = users });
    }

    private static async Task<IResult> DeactivateUserAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid userId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] FlitDbContext db,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var belongs = await db.UserRoleAssignments.AsNoTracking()
            .AnyAsync(a => a.TenantId == childTenantId && a.UserId == userId && a.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (!belongs)
        {
            return GroupHeadTenantAccess.Forbidden();
        }

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (target is null || target.DeletedAt is not null)
        {
            return Results.NotFound();
        }

        target.Status = "inactive";
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }

    private sealed record ChildInvitationRequest(string? Email, string? FullName, Guid[]? RoleIds);
}
