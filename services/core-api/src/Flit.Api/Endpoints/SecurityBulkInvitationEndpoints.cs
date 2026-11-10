using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Api.RateLimiting;
using Flit.Modules.Security.Application.Auth.BulkInvitation;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14424 (Feature #14420, ADR-0077) — invitación masiva por Excel para el perfil Gestor:
/// <c>GET /api/v1/security/invitations/bulk/template</c> y <c>POST /api/v1/security/invitations/bulk</c>.
/// Misma policy que <c>POST /security/invitations</c> (SuperAdmin o AdminCompany). SuperAdmin indica el destino con
/// <c>?targetTenantId=</c>; AdminCompany opera siempre sobre su propio tenant.
/// <para>
/// El endpoint solo arma el contexto de alcance y traduce resultados; la regla de roles vive en
/// <c>IInvitationScopePolicy</c> y la orquestación en <c>BulkInvitationService</c>. Auditoría: UNA fila
/// <c>users/bulk_invite</c> por commit (el dryRun no audita).
/// </para>
/// </summary>
public static class SecurityBulkInvitationEndpoints
{
    public static IEndpointRouteBuilder MapSecurityBulkInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/security/invitations/bulk")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .WithTags("Security · Invitaciones");

        // GET /template[?targetTenantId=] — AC1 → 200 (xlsx) / 400 / 401 / 403 / 404.
        group.MapGet("/template", TemplateAsync)
            .WithName("SecurityBulkInvitationTemplate")
            .WithSummary("Descarga la plantilla Excel de invitación masiva con el catálogo de roles asignables");

        // POST ?dryRun=true|false[&targetTenantId=] — multipart «file» (ADR-0077 / cliente FE). dryRun por defecto.
        group.MapPost(string.Empty, SubmitAsync)
            .DisableAntiforgery()
            .AddEndpointFilter(new CommitOnlyAuditFilter(new AdminAuditFilter(
                AuditVocabulary.Modules.Users, AuditVocabulary.Operations.BulkInvite, "invitation", "INVITATION")))
            .WithName("SecurityBulkInvitationSubmit")
            .WithSummary("Valida (dryRun=true, por defecto) o ejecuta (dryRun=false) la invitación masiva por Excel");

        return app;
    }

    private static async Task<IResult> TemplateAsync(
        ClaimsPrincipal caller,
        [FromQuery] Guid? targetTenantId,
        [FromServices] IBulkInvitationXlsxTemplate template,
        CancellationToken cancellationToken)
    {
        var (scope, failure) = BulkInvitationEndpointSupport.ResolveSecurityScope(caller, targetTenantId);
        return failure
            ?? await BulkInvitationEndpointSupport.TemplateAsync(scope!, template, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext httpContext,
        IFormFile? file,
        [FromQuery] Guid? targetTenantId,
        [FromServices] BulkInvitationService service,
        [FromServices] BulkInvitationOptions options,
        [FromServices] BulkInvitationDryRunLimiter dryRunLimiter,
        CancellationToken cancellationToken)
    {
        var (scope, failure) = BulkInvitationEndpointSupport.ResolveSecurityScope(httpContext.User, targetTenantId);
        if (failure is not null)
        {
            return failure;
        }

        return await BulkInvitationEndpointSupport
            .SubmitAsync(httpContext, file, scope!, "gestor", service, options, dryRunLimiter, cancellationToken)
            .ConfigureAwait(false);
    }
}
