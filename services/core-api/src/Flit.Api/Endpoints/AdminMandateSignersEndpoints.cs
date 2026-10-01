using System.Security.Claims;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerImpact;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerSignatureImage;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;
using Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de mandatarios (firmantes de mandato) por organismo de tránsito (ADR-0023,
/// RF22–RF28, RF33, RF34). Módulo Admin OT: SuperAdmin u ot_admin (<see cref="AdminAuthorization.OtModulePolicy"/>).
/// El número de documento es PII (Ley 1581): nunca se escribe en logs ni en mensajes de error.
/// </summary>
public static class AdminMandateSignersEndpoints
{
    public static IEndpointRouteBuilder MapAdminMandateSignersEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            // Bug #12912 (IDOR) — solo el organismo del perfil del usuario; SuperAdmin libre.
            .AddEndpointFilter<TransitOfficeScopeFilter>()
            // HU #13195 — el índice «un activo por origen» responde 409, no 500.
            .AddEndpointFilter<MandateSignerLinkConflictFilter>()
            .WithTags("Admin · Mandatarios");

        // GET — mandatarios activos del OT con sus compañías (RF27).
        group.MapGet("", ListAsync)
            .WithName("AdminMandateSignersList")
            .WithSummary("Lista los mandatarios activos de un organismo de tránsito")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /companies — compañías del OT con su mandatario resuelto (RF34 + multiselect).
        group.MapGet("/companies", ListCompaniesAsync)
            .WithName("AdminMandateSignersCompanies")
            .WithSummary("Lista las compañías del OT con su mandatario asignado (vista consolidada)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST — alta de mandatario (RF22).
        group.MapPost("", CreateAsync)
            .WithName("AdminMandateSignersCreate")
            // HU #13123 — escritura solo para ot_admin o SuperAdmin (gestor_tramites_ot recibe 403).
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Registra un mandatario en el organismo de tránsito")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /{signerId} — edición (RF23, regenera huella).
        group.MapPut("/{mandateSignerId:guid}", UpdateAsync)
            .WithName("AdminMandateSignersUpdate")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Edita un mandatario (regenera la huella de integridad)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // POST /{signerId}/inactivate — baja lógica que libera compañías (RF24).
        group.MapPost("/{mandateSignerId:guid}/inactivate", InactivateAsync)
            .WithName("AdminMandateSignersInactivate")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Inactiva un mandatario y libera sus compañías")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // POST /{signerId}/reactivate — reactiva un mandatario inactivado y restaura sus vínculos (HU #13136).
        group.MapPost("/{mandateSignerId:guid}/reactivate", ReactivateAsync)
            .WithName("AdminMandateSignersReactivate")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Reactiva un mandatario inactivado y restaura sus vínculos sin desplazar el default vigente")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // DELETE /{signerId} — eliminación (baja lógica deleted_at) con confirmación del impacto (HU #13135).
        group.MapDelete("/{mandateSignerId:guid}", DeleteAsync)
            .WithName("AdminMandateSignersDelete")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Elimina (baja lógica) un mandatario; con impacto exige confirmarImpacto=true")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // GET /{signerId}/impact — qué perdería la baja del mandatario (solo lectura, HU #13135).
        group.MapGet("/{mandateSignerId:guid}/impact", ImpactAsync)
            .WithName("AdminMandateSignersImpact")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Impacto de dar de baja al mandatario: únicos activos, defaults y trámites sin aprobar")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{mandateSignerId:guid}/signature-image", GetSignatureImageAsync)
            .WithName("AdminMandateSignerSignatureImage")
            .WithSummary("Devuelve el PNG de la firma del baúl del mandatario")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // HU #13131 (ADR-0061) — reporte de migración de la firma física. SOLO Super Admin (403 al resto):
        // cruza compañías y organismos, y un ot_admin no debe ver datos de otros tenants.
        app.MapGroup("/api/v1/admin/mandate-signers")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Mandatarios")
            .MapGet("/physical-signature-migration-report", PhysicalSignatureMigrationReportAsync)
            .WithName("AdminMandateSignersPhysicalSignatureMigrationReport")
            .WithSummary("Mandatarios activos que dependen solo de la firma física (filtrable por organismo, exportable a CSV)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // HU #13195 (ADR-0066 D1) — reporte PREVIO de solo lectura del colapso de vínculos (un activo por origen).
        // SOLO Super Admin (403 al resto); sin datos personales: ids, organismo, compañía y qué se conserva.
        app.MapGroup("/api/v1/admin/mandate-signers")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Mandatarios")
            .MapGet("/link-collapse-report", LinkCollapseReportAsync)
            .WithName("AdminMandateSignersLinkCollapseReport")
            .WithSummary("Reporte previo (solo lectura) de los vínculos mandatario-compañía que el colapso inactivaría")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// HU #13131 — <c>?transitOfficeId=</c> filtra por organismo; <c>?format=csv</c> exporta. La respuesta no
    /// incluye documento ni correo del mandatario (Ley 1581) y nada de esto se escribe en logs.
    /// </summary>
    private static async Task<IResult> PhysicalSignatureMigrationReportAsync(
        [FromQuery] Guid? transitOfficeId,
        [FromQuery] string? format,
        [FromServices] GetPhysicalSignatureMigrationReportHandler handler,
        CancellationToken cancellationToken)
    {
        var rows = await handler.HandleAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = PhysicalSignatureMigrationCsv.Build(rows);
            return Results.File(
                System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv; charset=utf-8",
                PhysicalSignatureMigrationCsv.FileName);
        }

        return Results.Ok(new { data = rows, total = rows.Count });
    }

    /// <summary>HU #13195 — <c>?transitOfficeId=</c> filtra por organismo. No modifica datos.</summary>
    private static async Task<IResult> LinkCollapseReportAsync(
        [FromQuery] Guid? transitOfficeId,
        [FromServices] GetMandateLinkCollapseReportHandler handler,
        CancellationToken cancellationToken)
    {
        var rows = await handler.HandleAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new
        {
            data = rows,
            total = rows.Count,
            toInactivate = rows.Count(r => r.Action == Flit.Admin.Domain.Companies.MandateSigners.MandateLinkCollapseActions.Inactivar),
        });
    }

    private static async Task<IResult> ListAsync(
        Guid transitOfficeId,
        HttpContext httpContext,
        [FromServices] ListMandateSignersHandler handler,
        CancellationToken cancellationToken)
    {
        // Bug #12912 (Habeas Data) — el organismo solo ve los mandatarios y compañías que le competen.
        var query = new ListMandateSignersQuery
        {
            TransitOfficeId = transitOfficeId,
            Visibility = OtCompanyVisibilityPolicy.For(httpContext.User),
            // HU #13134 — origen y banderas puedeEditar/puedeEliminar según el rol de quien consulta.
            ActorKind = MandateSignerActors.ForHub(httpContext.User),
        };
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        // HU #11764 (ADR-0050) — se retira `mockIdentityEnabled`: el botón "Simular validación" ya no
        // existe (su ruta responde 410 Gone) y el flag no tenía otro consumidor.
        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> ListCompaniesAsync(
        Guid transitOfficeId,
        HttpContext httpContext,
        [FromServices] ListOtCompaniesHandler handler,
        CancellationToken cancellationToken)
    {
        // Bug #12912 (Ley 1581) — el organismo solo ve por nombre la red que ya le entregó trámites.
        var query = new ListOtCompaniesQuery
        {
            TransitOfficeId = transitOfficeId,
            Visibility = OtCompanyVisibilityPolicy.For(httpContext.User),
        };
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> CreateAsync(
        Guid transitOfficeId,
        CreateMandateSignerRequest request,
        HttpContext httpContext,
        [FromServices] CreateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        if (TransitOfficeScopeFilter.BodyOfficesOutOfScope(httpContext.User, transitOfficeId, request.TransitOfficeIds))
        {
            return TransitOfficeScopeFilter.Forbidden();
        }

        var command = new CreateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            FullName = request.FullName ?? string.Empty,
            DocumentNumber = request.DocumentNumber ?? string.Empty,
            CompanyTenantIds = request.CompanyTenantIds ?? [],
            DocumentType = request.DocumentType ?? "CC",
            Email = request.Email,
            UserId = request.UserId,
            // HU #11201 — la misma persona puede firmar en varios organismos.
            TransitOfficeIds = request.TransitOfficeIds,
            // HU #13123 — firma del baúl (solo el id) y validaciones compartidas con la compañía.
            SignatureVaultId = request.SignatureVaultId,
            // HU #13129 — modelo, forma de firma y vigencia propia.
            SignerModel = request.SignerModel,
            SignatureMethod = request.SignatureMethod,
            ValidityKind = request.ValidityKind,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            ValidateSigningMeans = true,
            CreatedBy = MandateEndpointHelpers.ResolveUserId(httpContext.User),
            // HU #13195 — origen del vínculo: Super Admin → super_admin; ot_admin → organismo.
            ConfiguredByScope = OrigenDelActor(httpContext.User),
            CompanyVisibility = OtCompanyVisibilityPolicy.For(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{result.MandateSignerId}",
                new
                {
                    id = result.MandateSignerId,
                    integrityHash = result.IntegrityHash,
                    // HU #11000 — desenlace de la validación de identidad disparada por el alta, para que
                    // el aviso al usuario sea veraz ("enviada" / "ya validada" / "no se pudo enviar").
                    identity = result.Identity.ToString().ToLowerInvariant(),
                    // Ajuste HU #13123 — solo el NOMBRE del medio resuelto ("baul"/"biometria"); el OT
                    // nunca recibe id de firma, imagen ni metadatos del baúl.
                    signingMeans = result.SigningMeans,
                })
            : ValidationProblem(result.Errors);
    }

    private static async Task<IResult> UpdateAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        UpdateMandateSignerRequest request,
        HttpContext httpContext,
        [FromServices] UpdateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        if (TransitOfficeScopeFilter.BodyOfficesOutOfScope(httpContext.User, transitOfficeId, request.TransitOfficeIds))
        {
            return TransitOfficeScopeFilter.Forbidden();
        }

        var command = new UpdateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            MandateSignerId = mandateSignerId,
            FullName = request.FullName ?? string.Empty,
            DocumentNumber = request.DocumentNumber ?? string.Empty,
            CompanyTenantIds = request.CompanyTenantIds ?? [],
            DocumentType = request.DocumentType ?? "CC",
            Email = request.Email,
            UserId = request.UserId,
            TransitOfficeIds = request.TransitOfficeIds,
            // HU #13129 — modelo, forma de firma y vigencia propia (ausentes ⇒ se conserva lo guardado).
            SignerModel = request.SignerModel,
            SignatureMethod = request.SignatureMethod,
            ValidityKind = request.ValidityKind,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            UpdatedBy = MandateEndpointHelpers.ResolveUserId(httpContext.User),
            ConfiguredByScope = OrigenDelActor(httpContext.User),
            CompanyVisibility = OtCompanyVisibilityPolicy.For(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateMandateSignerOutcome.Updated =>
                Results.Ok(new { id = mandateSignerId, integrityHash = result.IntegrityHash }),
            UpdateMandateSignerOutcome.NotFound =>
                Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId} en este organismo." }),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static async Task<IResult> InactivateAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromServices] InactivateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new InactivateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            MandateSignerId = mandateSignerId,
            ChangedBy = MandateEndpointHelpers.ResolveUserId(httpContext.User),
            ActorKind = MandateSignerActors.ForHub(httpContext.User),
        };

        var result = await handler.HandleDetailedAsync(command, cancellationToken).ConfigureAwait(false);

        return MandateSignerLifecycleResponses.ForInactivate(httpContext, result, NotInOffice(mandateSignerId));
    }

    private static async Task<IResult> ReactivateAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromServices] ReactivateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new ReactivateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            MandateSignerId = mandateSignerId,
            ChangedBy = MandateEndpointHelpers.ResolveUserId(httpContext.User),
            ActorKind = MandateSignerActors.ForHub(httpContext.User),
        };

        var result = await handler.HandleDetailedAsync(command, cancellationToken).ConfigureAwait(false);

        return MandateSignerLifecycleResponses.ForReactivate(result, NotInOffice(mandateSignerId));
    }

    private static async Task<IResult> DeleteAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromQuery] bool? confirmarImpacto,
        [FromServices] DeleteMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new DeleteMandateSignerCommand
            {
                TransitOfficeId = transitOfficeId,
                MandateSignerId = mandateSignerId,
                ConfirmImpact = confirmarImpacto ?? false,
                ChangedBy = MandateEndpointHelpers.ResolveUserId(httpContext.User),
                ActorKind = MandateSignerActors.ForHub(httpContext.User),
            },
            cancellationToken).ConfigureAwait(false);

        return MandateSignerLifecycleResponses.ForDelete(httpContext, result, NotInOffice(mandateSignerId));
    }

    private static async Task<IResult> ImpactAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        [FromServices] GetMandateSignerImpactHandler handler,
        CancellationToken cancellationToken)
    {
        var impact = await handler.HandleAsync(transitOfficeId, mandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        return impact is null
            ? Results.NotFound(new { error = NotInOffice(mandateSignerId) })
            : Results.Ok(new { data = MandateSignerLifecycleResponses.ImpactBody(impact) });
    }

    private static string NotInOffice(Guid mandateSignerId) =>
        $"No existe el mandatario {mandateSignerId} en este organismo.";

    private static async Task<IResult> GetSignatureImageAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        [FromServices] GetMandateSignerSignatureImageHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(transitOfficeId, mandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            GetMandateSignerSignatureImageOutcome.Ok =>
                Results.File(result.Content!, "image/png"),
            GetMandateSignerSignatureImageOutcome.NotFound =>
                Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId} en este organismo." }),
            _ => Results.NotFound(new { error = "Este mandatario no tiene imagen de firma en el baúl." }),
        };
    }

    /// <summary>422 con el sobre estándar de errores; nunca incluye PII.</summary>
    private static IResult ValidationProblem(
        IReadOnlyList<Flit.Admin.Application.Companies.MandateSigners.MandateSignerValidationError> errors) =>
        Results.Json(
            new { errors = errors.Select(e => new { field = e.Field, message = e.Message, value = e.Value }) },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>HU #13195 — origen de configuración según quien actúa en la ruta del OT.</summary>
    private static string OrigenDelActor(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole) ? "super_admin" : "organismo";
}
