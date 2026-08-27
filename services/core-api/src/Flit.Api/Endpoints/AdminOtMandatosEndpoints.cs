using System.Security.Claims;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.OtProfile;
using Flit.Api.Authorization;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Config de mandato en el hub OT. Misma persistencia que Plataforma → Mandatos; ot_admin
/// solo sobre su organismo.
/// </summary>
public static class AdminOtMandatosEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtMandatosEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot/offices/{officeId:guid}/mandatos")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · OT · Mandatos");

        group.MapGet("", GetAsync).WithName("AdminOtMandatosGet");
        group.MapPut("", UpsertAsync).WithName("AdminOtMandatosUpsert");
        group.MapGet("/preview", PreviewOtAsync).WithName("AdminOtMandatosPreview");
        group.MapGet("/company-rules", ListCompanyRulesAsync).WithName("AdminOtMandatosListCompanyRules");
        group.MapPut("/company-rules/{companyTenantId:guid}", UpsertCompanyRuleAsync)
            .WithName("AdminOtMandatosUpsertCompanyRule");
        group.MapDelete("/company-rules/{companyTenantId:guid}", DeleteCompanyRuleAsync)
            .WithName("AdminOtMandatosDeleteCompanyRule");
        group.MapGet("/templates/{templateCode}/preview", PreviewTemplateAsync)
            .WithName("AdminOtMandatosTemplatePreview");

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid officeId,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        var view = await service.GetAsync(officeId, ct).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }

    private static async Task<IResult> UpsertAsync(
        Guid officeId,
        [FromBody] UpsertMandateOtConfigRequest request,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        var (status, view) = await service
            .UpsertAsync(officeId, request, ResolveUserId(user), ct)
            .ConfigureAwait(false);
        return MapWrite(status, view);
    }

    private static async Task<IResult> PreviewOtAsync(
        Guid officeId,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandateConfigAdminService service,
        IMandatoGenerator generator,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        var view = await service.GetAsync(officeId, ct).ConfigureAwait(false);
        if (view is null)
            return Results.NotFound();

        byte[]? customPdf = null;
        if (view.CustomTemplateKind == MandatoCustomTemplateKindCodes.Pdf)
            customPdf = await service.OpenCustomPdfAsync(officeId, ct).ConfigureAwait(false);

        var doc = generator.GenerateMandato(
            AdminPlataformaMandatosEndpoints.BuildOtPreviewData(view, customPdf));
        return Results.File(doc.Content, contentType: "application/pdf");
    }

    private static async Task<IResult> ListCompanyRulesAsync(
        Guid officeId,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        if (await service.GetAsync(officeId, ct).ConfigureAwait(false) is null)
            return Results.NotFound();

        var items = await service.ListCompanyRulesAsync(officeId, ct).ConfigureAwait(false);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> UpsertCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        [FromBody] UpsertCompanyOtMandateRuleRequest request,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        var (status, view) = await service
            .UpsertCompanyRuleAsync(officeId, companyTenantId, request, ResolveUserId(user), ct)
            .ConfigureAwait(false);
        return status switch
        {
            MandateConfigWriteStatus.Ok => Results.Ok(view),
            MandateConfigWriteStatus.OfficeNotFound or MandateConfigWriteStatus.CompanyNotFound =>
                Results.NotFound(),
            MandateConfigWriteStatus.InvalidAssignmentMode =>
                Results.BadRequest(new { error = "assignment_mode_invalido" }),
            MandateConfigWriteStatus.InvalidFamily =>
                Results.BadRequest(new { error = "mandatary_family_invalida" }),
            MandateConfigWriteStatus.InstitutionalRequired =>
                Results.BadRequest(new { error = "mandatario_institucional_requerido" }),
            MandateConfigWriteStatus.InvalidDefaultSigner =>
                Results.BadRequest(new { error = "mandatario_default_invalido" }),
            _ => Results.BadRequest(),
        };
    }

    private static async Task<IResult> DeleteCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        var status = await service.DeleteCompanyRuleAsync(officeId, companyTenantId, ct).ConfigureAwait(false);
        return status == MandateConfigWriteStatus.Ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> PreviewTemplateAsync(
        Guid officeId,
        string templateCode,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandatoGenerator generator,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        var code = templateCode?.Trim() ?? string.Empty;
        if (code is not (
            MandatoTemplateResolver.Generico or MandatoTemplateResolver.Sabaneta
            or MandatoTemplateResolver.Bello or MandatoTemplateResolver.Municipio))
        {
            return Results.BadRequest(new { error = "template_code_invalido" });
        }

        var doc = generator.GenerateMandato(MandatoPreviewSample.Build(code));
        return Results.File(doc.Content, contentType: "application/pdf");
    }

    private static async Task<IResult?> ForbidIfOfficeOutOfScopeAsync(
        ClaimsPrincipal user,
        Guid transitOfficeId,
        IOtProfileRepository profileRepository,
        CancellationToken cancellationToken)
    {
        if (user.IsInRole(AdminAuthorization.SuperAdminRole))
            return null;

        if (Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId))
        {
            var profile = await profileRepository
                .GetByTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (profile is not null && profile.TransitOfficeId == transitOfficeId)
                return null;
        }

        return Results.Json(
            new { code = "TRANSIT_OFFICE_FORBIDDEN" },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult MapWrite(MandateConfigWriteStatus status, MandateOtConfigView? view) =>
        status switch
        {
            MandateConfigWriteStatus.Ok => Results.Ok(view),
            MandateConfigWriteStatus.OfficeNotFound => Results.NotFound(),
            MandateConfigWriteStatus.CompanyNotFound => Results.NotFound(),
            MandateConfigWriteStatus.Conflict => Results.Conflict(new { error = "row_version_conflict" }),
            MandateConfigWriteStatus.InvalidTemplate => Results.BadRequest(new { error = "template_code_invalido" }),
            MandateConfigWriteStatus.InvalidFamily => Results.BadRequest(new { error = "mandatary_family_invalida" }),
            MandateConfigWriteStatus.InvalidAssignmentMode =>
                Results.BadRequest(new { error = "assignment_mode_invalido" }),
            MandateConfigWriteStatus.InstitutionalRequired =>
                Results.BadRequest(new { error = "mandatario_institucional_requerido" }),
            MandateConfigWriteStatus.InvalidDefaultSigner =>
                Results.BadRequest(new { error = "mandatario_default_invalido" }),
            _ => Results.BadRequest(),
        };

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
