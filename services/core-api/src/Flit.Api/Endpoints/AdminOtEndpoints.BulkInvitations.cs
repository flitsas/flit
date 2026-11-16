using Flit.Api.Authorization;
using Flit.Api.RateLimiting;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Application.Auth.BulkInvitation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14425 (Feature #14420, ADR-0077) — espejo OT de la invitación masiva:
/// <c>GET /api/v1/admin/ot/users/invite/bulk/template</c> y <c>POST /api/v1/admin/ot/users/invite/bulk</c>.
/// <list type="bullet">
///   <item>Autorización: <c>OtModulePolicy</c> del grupo AND <c>ot.usuarios.manage</c> (<see cref="OtPermissionMap"/>), igual
///   que <c>POST /users/invite</c>: un Gestor OT sin «Usuarios» delegado recibe 403 y no se procesa nada.</item>
///   <item>Destino: el tenant OT del JWT. SuperAdmin: <c>?transitOfficeId=</c> obligatorio
///   (<see cref="ResolveOtUserScopeAsync"/>). A diferencia de <c>/users/invite</c> —que ignora un <c>transitOfficeId</c>
///   ajeno— aquí un <c>ot_admin</c> que apunta a OTRA OT recibe 403 (HU #14425 AC3).</item>
///   <item>Roles: <c>IInvitationScopePolicy</c> en el canal OT: solo roles TRANSIT_OFFICE; el Gestor OT nunca recibe ni
///   puede asignar roles de administrador (la fila se rechaza y las demás siguen).</item>
///   <item>Auditoría: una fila <c>users/bulk_invite</c> por commit; el dryRun no audita.</item>
/// </list>
/// </summary>
public static partial class AdminOtEndpoints
{
    private static async Task<IResult> BulkInviteTemplateAsync(
        HttpContext httpContext,
        [FromQuery] Guid? transitOfficeId,
        FlitDbContext db,
        [FromServices] IBulkInvitationXlsxTemplate template,
        CancellationToken cancellationToken)
    {
        var (scope, failure) = await ResolveBulkInviteScopeAsync(httpContext, transitOfficeId, db, cancellationToken)
            .ConfigureAwait(false);

        return failure
            ?? await BulkInvitationEndpointSupport.TemplateAsync(scope!, template, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> BulkInviteSubmitAsync(
        HttpContext httpContext,
        IFormFile? file,
        [FromQuery] Guid? transitOfficeId,
        FlitDbContext db,
        [FromServices] BulkInvitationService service,
        [FromServices] BulkInvitationOptions options,
        [FromServices] BulkInvitationDryRunLimiter dryRunLimiter,
        CancellationToken cancellationToken)
    {
        var (scope, failure) = await ResolveBulkInviteScopeAsync(httpContext, transitOfficeId, db, cancellationToken)
            .ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        return await BulkInvitationEndpointSupport
            .SubmitAsync(httpContext, file, scope!, "ot", service, options, dryRunLimiter, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(InvitationScopeContext? Scope, IResult? Failure)> ResolveBulkInviteScopeAsync(
        HttpContext httpContext,
        Guid? transitOfficeId,
        FlitDbContext db,
        CancellationToken cancellationToken)
    {
        var user = httpContext.User;

        if (!RequestTenantResolver.TryResolveTenantId(user, out var callerTenantId)
            || BulkInvitationEndpointSupport.ResolveUserId(user) is not { } userId)
        {
            return (null, Results.Unauthorized());
        }

        Guid targetTenantId;
        if (BulkInvitationEndpointSupport.IsSuperAdmin(user))
        {
            var (tenantId, scopeError) = await ResolveOtUserScopeAsync(user, transitOfficeId, db, cancellationToken)
                .ConfigureAwait(false);
            if (scopeError is not null)
            {
                return (null, scopeError);
            }

            targetTenantId = tenantId;
        }
        else
        {
            targetTenantId = callerTenantId;

            // Fail-closed: un invitador que no es SuperAdmin solo opera su propio organismo. Si indica otra OT (o una
            // que no existe) se rechaza; no se ignora en silencio, como sí hace /users/invite.
            if (transitOfficeId is { } requested && requested != Guid.Empty)
            {
                var officeTenantId = await db.TransitOfficeProfiles
                    .AsNoTracking()
                    .Where(p => p.TransitOfficeId == requested)
                    .Select(p => p.TenantId)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (officeTenantId != callerTenantId)
                {
                    return (null, BulkInvitationEndpointSupport.DeniedResult(InvitationScopeReasons.TargetTenantOutOfScope));
                }
            }
        }

        return (
            new InvitationScopeContext(
                userId,
                callerTenantId,
                BulkInvitationEndpointSupport.RoleCodes(user),
                targetTenantId,
                InvitationChannel.Ot),
            null);
    }
}
