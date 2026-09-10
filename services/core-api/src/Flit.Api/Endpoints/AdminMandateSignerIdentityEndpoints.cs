using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #11758 (ADR-0050) — el módulo Identidad es la única fuente de verdad; el área admin deja de
/// disparar y de vincular validaciones. Las cuatro rutas que aquí vivían (<c>send</c>, <c>resend</c>,
/// <c>link</c>, <c>mock</c>; HU #10911/#11028) quedan RETIRADAS y responden <c>410 Gone</c> con
/// <c>code: endpoint_deprecado</c>: el cliente distingue «esto existió y se retiró» (410) de «esto
/// nunca existió» (404) — decisión DA-1 del ADR-0050.
/// </summary>
public static class AdminMandateSignerIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAdminMandateSignerIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers/{id:guid}/identity")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · Identidad de Mandatarios");

        group.MapPost("/send", DeprecatedAdminIdentityEndpoints.Gone)
            .WithName("AdminMandateSignerIdentitySend")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/resend", DeprecatedAdminIdentityEndpoints.Gone)
            .WithName("AdminMandateSignerIdentityResend")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/link", DeprecatedAdminIdentityEndpoints.Gone)
            .WithName("AdminMandateSignerIdentityLink")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/mock", DeprecatedAdminIdentityEndpoints.Gone)
            .WithName("AdminMandateSignerIdentityMock")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        return app;
    }
}
