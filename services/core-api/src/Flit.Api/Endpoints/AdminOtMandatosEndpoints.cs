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
        group.MapPatch("/default-signer", SetDefaultSignerAsync).WithName("AdminOtMandatosSetDefaultSigner");
        group.MapGet("/preview", PreviewOtAsync).WithName("AdminOtMandatosPreview");
        group.MapGet("/company-rules", ListCompanyRulesAsync).WithName("AdminOtMandatosListCompanyRules");
        group.MapPatch("/company-rules/{companyTenantId:guid}/default-signer", SetCompanyDefaultSignerAsync)
            .WithName("AdminOtMandatosSetCompanyDefaultSigner");
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

    private static async Task<IResult> SetDefaultSignerAsync(
        Guid officeId,
        [FromBody] SetOtDefaultSignerRequest request,
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
            .SetOtDefaultSignerAsync(officeId, request, MandateEndpointHelpers.ResolveUserId(user), ct)
            .ConfigureAwait(false);
        return MandateEndpointHelpers.MapWrite(status, view);
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

        // Bug #12912 (Ley 1581) — el organismo solo ve por nombre la red que ya le entregó trámites.
        var items = await service
            .ListCompanyRulesAsync(officeId, OtCompanyVisibilityPolicy.For(user), ct)
            .ConfigureAwait(false);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> SetCompanyDefaultSignerAsync(
        Guid officeId,
        Guid companyTenantId,
        [FromBody] SetCompanyDefaultSignerRequest request,
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
            .SetCompanyDefaultSignerAsync(
                officeId, companyTenantId, request, MandateEndpointHelpers.ResolveUserId(user), OtCompanyVisibilityPolicy.For(user), ct)
            .ConfigureAwait(false);
        return status switch
        {
            MandateConfigWriteStatus.Ok => Results.Ok(view),
            MandateConfigWriteStatus.OfficeNotFound or MandateConfigWriteStatus.CompanyNotFound =>
                Results.NotFound(),
            MandateConfigWriteStatus.InvalidDefaultSigner =>
                Results.BadRequest(new { error = "mandatario_default_invalido" }),
            // HU #13148 — rowVersion opcional en el hub: solo choca si el cliente lo envía y ya cambió.
            MandateConfigWriteStatus.Conflict =>
                Results.Conflict(new { error = "row_version_conflict" }),
            _ => Results.BadRequest(),
        };
    }

    private static async Task<IResult> DeleteCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        [FromQuery] long? rowVersion,
        ClaimsPrincipal user,
        IOtProfileRepository profiles,
        IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var forbidden = await ForbidIfOfficeOutOfScopeAsync(user, officeId, profiles, ct)
            .ConfigureAwait(false);
        if (forbidden is not null)
            return forbidden;

        var status = await service
            .DeleteCompanyRuleAsync(officeId, companyTenantId, OtCompanyVisibilityPolicy.For(user), rowVersion, ct: ct)
            .ConfigureAwait(false);
        return status switch
        {
            MandateConfigWriteStatus.Ok => Results.NoContent(),
            MandateConfigWriteStatus.Conflict => Results.Conflict(new { error = "row_version_conflict" }),
            _ => Results.NotFound(),
        };
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
        if (!MandatoTemplateResolver.IsRedaction(code))
            return MandateEndpointHelpers.InvalidTemplateCode();

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

        if (RequestTenantResolver.TryResolveTenantId(user, out var tenantId))
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
}
