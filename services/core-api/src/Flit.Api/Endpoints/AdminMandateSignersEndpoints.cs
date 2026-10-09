using System.Security.Claims;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerImpact;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerSignatureImage;
using Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;
using Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.RepresentedAssociations;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
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

        // HU #13246 — «Reenviar validación» de la identidad propia del mandatario (ruta NUEVA; identity/send|resend|link
        // siguen en 410). Mismo permiso de gestión que editar: ot_admin o Super Admin.
        // «Consultar estado» de la validación propia: pregunta a Kyverum y aplica el resultado si el webhook no llegó, como la
        // pantalla de espera del trámite.
        group.MapPost("/{mandateSignerId:guid}/identity-validation/reconcile", ReconcileIdentityAsync)
            .WithName("AdminMandateSignersIdentityValidationReconcile")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Consulta al proveedor el estado de la validación de identidad propia del mandatario")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{mandateSignerId:guid}/identity-validation/resend", ResendIdentityAsync)
            .WithName("AdminMandateSignersIdentityValidationResend")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithSummary("Reenvía la validación de identidad propia del mandatario (Persona natural con biometría)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapGet("/{mandateSignerId:guid}/signature-image", GetSignatureImageAsync)
            .WithName("AdminMandateSignerSignatureImage")
            .WithSummary("Devuelve el PNG de la firma del baúl del mandatario")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // HU #13178 (Feature #13119 F7) — directorio de compañías asociables para el OT y el Super Admin: TODAS las
        // compañías gestoras activas (búsqueda por nombre y NIT), solo id, nombre y NIT. Política más estricta que
        // OtModule: un Gestor del OT recibe 403. No toca la visibilidad de la bandeja (Bug #12912).
        app.MapGroup("/api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .AddEndpointFilter<TransitOfficeScopeFilter>()
            .WithTags("Admin · Mandatarios")
            .MapGet("/associable-companies", AssociableCompaniesAsync)
            .WithName("AdminMandateSignersAssociableCompanies")
            .WithSummary("Compañías gestoras activas a las que se puede asociar un mandatario (OT y Super Admin)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

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

        // HU #13247 (Feature #13245) — mandatarios afectados por la validación exclusiva (Persona natural con biometría sin
        // validación propia aprobada), con compañía, organismo y correo para avisarles. SOLO Super Admin (403 al resto).
        app.MapGroup("/api/v1/admin/mandate-signers")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Mandatarios")
            .MapGet("/identity-validation-report", IdentityValidationReportAsync)
            .WithName("AdminMandateSignersIdentityValidationReport")
            .WithSummary("Mandatarios con biometría sin validación propia aprobada (filtrable por organismo, exportable a CSV)")
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

        // HU #13176 (Feature #13119 F7) — reporte de mandatarios impactados por el retiro de las asociaciones por
        // Representante Legal y retiro controlado. SOLO Super Admin (403 al resto). Orden de despliegue por
        // ambiente: reporte, aviso a los clientes, retiro.
        var representedGroup = app.MapGroup("/api/v1/admin/mandate-signers/represented-associations")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Mandatarios");

        representedGroup.MapGet("/impact-report", RepresentedAssociationImpactReportAsync)
            .WithName("AdminMandateSignersRepresentedAssociationImpactReport")
            .WithSummary("Mandatarios que dependen de una asociación por Representante Legal (JSON o ?format=csv)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        representedGroup.MapPost("/retire", RetireRepresentedAssociationsAsync)
            .WithName("AdminMandateSignersRepresentedAssociationRetire")
            .WithSummary("Retira las asociaciones por Representante Legal; exige confirmaAvisoEnviado=true (409 aviso_no_confirmado)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

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

    /// <summary>
    /// HU #13247 — <c>?transitOfficeId=</c> filtra por organismo; <c>?format=csv</c> exporta. Incluye el correo del
    /// mandatario (PII, Ley 1581) para poder avisarle: solo Super Admin y nunca se escribe en logs.
    /// </summary>
    private static async Task<IResult> IdentityValidationReportAsync(
        [FromQuery] Guid? transitOfficeId,
        [FromQuery] string? format,
        [FromServices] GetMandateIdentityAffectedReportHandler handler,
        CancellationToken cancellationToken)
    {
        var rows = await handler.HandleAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = MandateIdentityAffectedCsv.Build(rows);
            return Results.File(
                System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv; charset=utf-8",
                MandateIdentityAffectedCsv.FileName);
        }

        return Results.Ok(new { data = rows, total = rows.Count });
    }

    private static async Task<IResult> ReconcileIdentityAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        [FromServices] ReconcileMandateSignerIdentityHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(mandateSignerId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        return MandateSignerIdentityHttp.ToResult(result, mandateSignerId);
    }

    private static async Task<IResult> ResendIdentityAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        [FromServices] ResendMandateSignerIdentityHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(mandateSignerId, transitOfficeId, null, cancellationToken)
            .ConfigureAwait(false);
        return MandateSignerIdentityHttp.ToResult(result, mandateSignerId);
    }

    /// <summary>HU #13178 — <c>?search=</c> (mínimo 2 caracteres), <c>?page=</c>, <c>?pageSize=</c>, <c>?all=true</c> (lista completa, tope 1000).</summary>
    private static async Task<IResult> AssociableCompaniesAsync(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? all,
        [FromServices] IMandatarioAssociableCompanies service,
        CancellationToken cancellationToken)
    {
        var result = await service
            .ListForOtAsync(search, page ?? 1, AssociableCompaniesHttp.PageSizeOf(pageSize, all), cancellationToken)
            .ConfigureAwait(false);
        return AssociableCompaniesHttp.ToResult(result);
    }

    /// <summary>
    /// HU #13176 — reporte de impactados; <c>?format=csv</c> descarga el archivo para el aviso a los clientes.
    /// Sin documento ni ruta de firma; nada de esto se escribe en logs.
    /// </summary>
    private static async Task<IResult> RepresentedAssociationImpactReportAsync(
        [FromQuery] string? format,
        [FromServices] GetRepresentedAssociationImpactReportHandler handler,
        CancellationToken cancellationToken)
    {
        var rows = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = RepresentedAssociationImpactCsv.Build(rows);
            return Results.File(
                System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv; charset=utf-8",
                RepresentedAssociationImpactCsv.FileName);
        }

        return Results.Ok(new { data = rows, total = rows.Count });
    }

    /// <summary>HU #13176 — el cuerpo puede faltar: sin <c>confirmaAvisoEnviado</c> verdadero responde 409.</summary>
    private static async Task<IResult> RetireRepresentedAssociationsAsync(
        HttpContext httpContext,
        [FromBody] RetireRepresentedAssociationsRequest? request,
        [FromServices] RetireRepresentedAssociationsHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(
                request?.ConfirmaAvisoEnviado == true,
                MandateEndpointHelpers.ResolveUserId(httpContext.User),
                httpContext.User.FindFirst("role")?.Value ?? AdminAuthorization.SuperAdminRole,
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { filasRetiradas = result.RetiredRows, fecha = result.ExecutedAt });
        }
        catch (RepresentedAssociationNoticeNotConfirmedException ex)
        {
            return Results.Json(
                new { code = RepresentedAssociationNoticeNotConfirmedException.Code, error = ex.Message },
                statusCode: StatusCodes.Status409Conflict);
        }
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
        if (TransitOfficeScopeFilter.BodyOfficesOutOfScope(
                httpContext.User, transitOfficeId, OficinasDelCuerpo(request.TransitOfficeIds, request.OfficeCompanies)))
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
            // HU #13179 — compañías de FLIT a las que se asocia el mandatario, por organismo.
            OfficeCompanies = request.OfficeCompanies,
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
        if (TransitOfficeScopeFilter.BodyOfficesOutOfScope(
                httpContext.User, transitOfficeId, OficinasDelCuerpo(request.TransitOfficeIds, request.OfficeCompanies)))
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
            // HU #13179 — ausente ⇒ no se tocan; cada organismo presente reemplaza su conjunto.
            OfficeCompanies = request.OfficeCompanies,
            // Firma del baúl del OT para su propio mandatario: solo se gestiona si el hub la manda.
            SignatureVaultId = request.SignatureVaultId,
            ActualizaFirma = request.SignatureVaultId is not null,
            UpdatedBy = MandateEndpointHelpers.ResolveUserId(httpContext.User),
            ConfiguredByScope = OrigenDelActor(httpContext.User),
            CompanyVisibility = OtCompanyVisibilityPolicy.For(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateMandateSignerOutcome.Updated =>
                Results.Ok(new
                {
                    id = mandateSignerId,
                    integrityHash = result.IntegrityHash,
                    // HU #13246 — sent | queued | failed cuando la edición lanzó una validación propia nueva (cambio de
                    // documento o paso de baúl a biometría); notattempted si no.
                    identity = result.Identity.ToString().ToLowerInvariant(),
                }),
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

    /// <summary>
    /// HU #13179 — organismos que nombra el cuerpo: los de <c>transitOfficeIds</c> y los de las compañías asociadas.
    /// Un ot_admin no puede escribir sobre organismos distintos al de su ruta.
    /// </summary>
    private static IReadOnlyList<Guid>? OficinasDelCuerpo(
        IReadOnlyList<Guid>? transitOfficeIds,
        IReadOnlyList<Flit.Admin.Domain.Companies.MandateSigners.MandateSignerOfficeCompanies>? officeCompanies) =>
        officeCompanies is null
            ? transitOfficeIds
            : [.. (transitOfficeIds ?? []), .. officeCompanies.Select(o => o.TransitOfficeId)];

    /// <summary>HU #13195 — origen de configuración según quien actúa en la ruta del OT.</summary>
    internal static string OrigenDelActor(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole) ? "super_admin" : "organismo";
}

/// <summary>HU #13176 — cuerpo del retiro. El aviso a los clientes lo envía el PO o soporte; aquí solo se confirma.</summary>
/// <param name="ConfirmaAvisoEnviado">Verdadero solo si el aviso a los clientes ya salió.</param>
public sealed record RetireRepresentedAssociationsRequest(bool? ConfirmaAvisoEnviado);
