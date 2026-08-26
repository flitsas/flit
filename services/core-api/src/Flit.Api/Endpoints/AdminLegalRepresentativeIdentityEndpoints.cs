using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #11758 (ADR-0050) — el módulo Identidad es la única fuente de verdad; el área admin deja de
/// disparar y de vincular validaciones. Las tres rutas que aquí vivían (<c>send</c>, <c>resend</c>,
/// <c>link</c>, HU #10907/#11176) quedan RETIRADAS y responden <c>410 Gone</c> con
/// <c>code: endpoint_deprecado</c>: el cliente distingue «esto existió y se retiró» (410) de «esto
/// nunca existió» (404) — decisión DA-1 del ADR-0050.
/// </summary>
public static class AdminLegalRepresentativeIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAdminLegalRepresentativeIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/legal-representatives/{id:guid}/identity")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            .WithTags("Admin · Identidad de Representantes");

        group.MapPost("/send", DeprecatedAdminIdentityEndpoints.GoneForTenant)
            .WithName("AdminLegalRepIdentitySend")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/resend", DeprecatedAdminIdentityEndpoints.GoneForTenant)
            .WithName("AdminLegalRepIdentityResend")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/link", DeprecatedAdminIdentityEndpoints.GoneForTenant)
            .WithName("AdminLegalRepIdentityLink")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        return app;
    }
}
