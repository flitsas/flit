using System.Security.Claims;
using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerImpact;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #11202 — mandatarios gestionados desde el configurador de la COMPAÑÍA. Es la vista inversa de
/// <see cref="AdminMandateSignersEndpoints"/>: el alta la hace la empresa y marca en cuáles de sus
/// organismos aplica el mandatario, en vez de que cada organismo elija compañías.
///
/// <para>El número de documento es PII (Ley 1581): nunca se escribe en logs ni en mensajes de error.</para>
/// </summary>
public static class AdminCompanyMandateSignersEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompanyMandateSignersEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/mandate-signers")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            // HU #13195 — el índice «un activo por origen» responde 409, no 500.
            .AddEndpointFilter<MandateSignerLinkConflictFilter>()
            .WithTags("Admin · Mandatarios de la compañía");

        group.MapGet("", ListAsync)
            .WithName("AdminCompanyMandateSignersList")
            .WithSummary("Lista los mandatarios de la compañía con sus organismos")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/transit-offices", ListTransitOfficesAsync)
            .WithName("AdminCompanyMandateSignersTransitOffices")
            .WithSummary("Organismos de tránsito habilitados para la compañía")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("", CreateAsync)
            .WithName("AdminCompanyMandateSignersCreate")
            .WithSummary("Registra un mandatario de la compañía en los organismos elegidos")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{mandateSignerId:guid}", UpdateAsync)
            .WithName("AdminCompanyMandateSignersUpdate")
            .WithSummary("Edita un mandatario de la compañía y sus organismos")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{mandateSignerId:guid}/inactivate", InactivateAsync)
            .WithName("AdminCompanyMandateSignersInactivate")
            .WithSummary("Inactiva un mandatario de la compañía, retira sus defaults y reasigna sus trámites sin aprobar")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{mandateSignerId:guid}/reactivate", ReactivateAsync)
            .WithName("AdminCompanyMandateSignersReactivate")
            .WithSummary("Reactiva un mandatario inactivado y restaura sus vínculos sin desplazar el default vigente")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // HU #13135 — baja lógica (deleted_at) con confirmación del impacto.
        group.MapDelete("/{mandateSignerId:guid}", DeleteAsync)
            .WithName("AdminCompanyMandateSignersDelete")
            .WithSummary("Elimina (baja lógica) un mandatario de la compañía; con impacto exige confirmarImpacto=true")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/{mandateSignerId:guid}/impact", ImpactAsync)
            .WithName("AdminCompanyMandateSignersImpact")
            .WithSummary("Impacto de dar de baja al mandatario (solo lectura)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // Empresas representadas de la compañía: las que se dan de alta dentro del formulario del
        // representante legal. Son la lista que el formulario del mandatario ofrece para acotar para
        // quién firma en cada organismo. Ya vienen únicas por (tenant, NIT).
        group.MapGet("/represented-companies", async (
                Guid tenantId,
                [FromServices] ILegalRepresentativeReader reader,
                CancellationToken ct) =>
            {
                var empresas = await reader.ListRepresentedCompaniesAsync(tenantId, ct).ConfigureAwait(false);
                return Results.Ok(new
                {
                    items = empresas.Select(e => new
                    {
                        id = e.Id,
                        documentNumber = e.DocumentNumber,
                        name = e.Name,
                    }),
                });
            })
            .WithName("AdminCompanyMandateSignerRepresentedCompanies")
            .WithSummary("Empresas representadas de la compañía, para acotar para quién firma el mandatario");

        // HU #11758 (ADR-0050) — las tres rutas de identidad del mandatario desde el configurador de la
        // COMPAÑÍA (send/resend/link) se RETIRAN: el módulo Identidad es la única fuente que puede
        // originar una validación. Responden 410 Gone, nunca 404 (decisión DA-1).
        group.MapPost("/{mandateSignerId:guid}/identity/send", DeprecatedAdminIdentityEndpoints.GoneForTenant)
            .WithName("AdminCompanyMandateSignerIdentitySend")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/{mandateSignerId:guid}/identity/resend", DeprecatedAdminIdentityEndpoints.GoneForTenant)
            .WithName("AdminCompanyMandateSignerIdentityResend")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/{mandateSignerId:guid}/identity/link", DeprecatedAdminIdentityEndpoints.GoneForTenant)
            .WithName("AdminCompanyMandateSignerIdentityLink")
            .WithSummary("Retirado (410) — la identidad se origina en el módulo Identidad, ADR-0050")
            .Produces(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        HttpContext httpContext,
        [FromServices] ListCompanyMandateSignersHandler handler,
        CancellationToken cancellationToken)
    {
        // HU #13134 — origen y banderas puedeEditar/puedeEliminar calculadas con el rol de quien consulta.
        var result = await handler
            .HandleAsync(tenantId, MandateSignerActors.ForCompany(httpContext.User), cancellationToken)
            .ConfigureAwait(false);
        // HU #11764 (ADR-0050) — se retira `mockIdentityEnabled`: el botón "Simular validación" ya no
        // existe (su ruta responde 410 Gone) y el flag no tenía otro consumidor.
        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> ListTransitOfficesAsync(
        Guid tenantId,
        [FromServices] ListCompanyTransitOfficesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CompanyMandateSignerRequest request,
        HttpContext httpContext,
        [FromServices] CreateCompanyMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(
                tenantId, request, MandateEndpointHelpers.ResolveUserId(httpContext.User), OrigenDeLaCompania(httpContext.User), cancellationToken)
            .ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{tenantId}/mandate-signers/{result.MandateSignerId}",
                new
                {
                    id = result.MandateSignerId,
                    integrityHash = result.IntegrityHash,
                    identity = result.Identity.ToString().ToLowerInvariant(),
                })
            : ValidationProblem(result.Errors);
    }

    private static async Task<IResult> UpdateAsync(
        Guid tenantId,
        Guid mandateSignerId,
        CompanyMandateSignerRequest request,
        HttpContext httpContext,
        [FromServices] MandateSignerAccessGuard guard,
        [FromServices] UpdateCompanyMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        // HU #13134 — candado: la compañía no edita lo que configuró el organismo de tránsito (403).
        var denied = await MandateSignerActors
            .CheckCompanyWriteAsync(guard, httpContext.User, tenantId, mandateSignerId, cancellationToken)
            .ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        var result = await handler
            .HandleAsync(
                tenantId, mandateSignerId, request, MandateEndpointHelpers.ResolveUserId(httpContext.User), OrigenDeLaCompania(httpContext.User), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateMandateSignerOutcome.Updated => Results.Ok(new { integrityHash = result.IntegrityHash }),
            UpdateMandateSignerOutcome.NotFound =>
                Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId}." }),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static Task<IResult> InactivateAsync(
        Guid tenantId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromServices] MandateSignerAccessGuard guard,
        [FromServices] ListCompanyMandateSignersHandler listHandler,
        [FromServices] InactivateMandateSignerHandler handler,
        CancellationToken cancellationToken) =>
        MandateSignerLifecycleResponses.CompanyInactivateAsync(
            httpContext, tenantId, mandateSignerId, guard, listHandler, handler, cancellationToken);

    private static Task<IResult> ReactivateAsync(
        Guid tenantId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromServices] MandateSignerAccessGuard guard,
        [FromServices] ListCompanyMandateSignersHandler listHandler,
        [FromServices] ReactivateMandateSignerHandler handler,
        CancellationToken cancellationToken) =>
        MandateSignerLifecycleResponses.CompanyReactivateAsync(
            httpContext, tenantId, mandateSignerId, guard, listHandler, handler, cancellationToken);

    private static Task<IResult> DeleteAsync(
        Guid tenantId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromQuery] bool? confirmarImpacto,
        [FromServices] MandateSignerAccessGuard guard,
        [FromServices] ListCompanyMandateSignersHandler listHandler,
        [FromServices] DeleteMandateSignerHandler handler,
        CancellationToken cancellationToken) =>
        MandateSignerLifecycleResponses.CompanyDeleteAsync(
            httpContext, tenantId, mandateSignerId, confirmarImpacto ?? false, guard, listHandler, handler,
            cancellationToken);

    private static Task<IResult> ImpactAsync(
        Guid tenantId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromServices] MandateSignerAccessGuard guard,
        [FromServices] ListCompanyMandateSignersHandler listHandler,
        [FromServices] GetMandateSignerImpactHandler handler,
        CancellationToken cancellationToken) =>
        MandateSignerLifecycleResponses.CompanyImpactAsync(
            httpContext, tenantId, mandateSignerId, guard, listHandler, handler, cancellationToken);

    /// <summary>422 con el sobre estándar de errores; nunca incluye PII.</summary>
    private static IResult ValidationProblem(IReadOnlyList<MandateSignerValidationError> errors) =>
        Results.Json(
            new { errors = errors.Select(e => new { field = e.Field, message = e.Message, value = e.Value }) },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>HU #13195c — origen del vínculo: Super Admin → super_admin; Admin de Compañía → compania.</summary>
    internal static string OrigenDeLaCompania(System.Security.Claims.ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole) ? "super_admin" : "compania";
}
