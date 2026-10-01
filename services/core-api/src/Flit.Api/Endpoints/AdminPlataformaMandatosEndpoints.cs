using System.Security.Claims;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → Mandatos: preview, CRUD de config por OT, plantilla propia y OCR.
/// </summary>
public static class AdminPlataformaMandatosEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlataformaMandatosEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plataforma/mandatos")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · Mandatos");

        group.MapGet("", ListAsync)
            .WithName("AdminPlataformaMandatosList")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/ot/{officeId:guid}", GetAsync)
            .WithName("AdminPlataformaMandatosGet")
            .Produces<MandateOtConfigView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/ot/{officeId:guid}", UpsertAsync)
            .WithName("AdminPlataformaMandatosUpsert")
            .Produces<MandateOtConfigView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPatch("/ot/{officeId:guid}/default-signer", SetDefaultSignerAsync)
            .WithName("AdminPlataformaMandatosSetDefaultSigner")
            .Produces<MandateOtConfigView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/ot/{officeId:guid}", DeleteAsync)
            .WithName("AdminPlataformaMandatosDelete")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/ot/{officeId:guid}/template", UploadTemplateAsync)
            .WithName("AdminPlataformaMandatosUploadTemplate")
            .DisableAntiforgery()
            .Produces<MandateOtConfigView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/ot/{officeId:guid}/template/editor", SaveEditorAsync)
            .WithName("AdminPlataformaMandatosSaveEditor")
            .Produces<MandateOtConfigView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/ot/{officeId:guid}/template", DeleteTemplateAsync)
            .WithName("AdminPlataformaMandatosDeleteTemplate")
            .Produces<MandateOtConfigView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{templateCode}/preview", PreviewTemplateAsync)
            .WithName("AdminPlataformaMandatosPreview")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/ot/{officeId:guid}/preview", PreviewOtAsync)
            .WithName("AdminPlataformaMandatosPreviewOt")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound);

        // Simulador (HU #11706): arma el escenario y genera / envía el PDF SIN tocar ningún trámite.
        group.MapGet("/simulador/ot/{officeId:guid}/mandatarios", ListSimulatorSignersAsync)
            .WithName("AdminPlataformaMandatosSimuladorSigners")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/simulador/preview", SimulatePreviewAsync)
            .WithName("AdminPlataformaMandatosSimuladorPreview")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/simulador/enviar", SimulateSendAsync)
            .WithName("AdminPlataformaMandatosSimuladorEnviar")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapGet("/ot/{officeId:guid}/company-rules", ListCompanyRulesAsync)
            .WithName("AdminPlataformaMandatosListCompanyRules")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/ot/{officeId:guid}/company-rules/{companyTenantId:guid}", UpsertCompanyRuleAsync)
            .WithName("AdminPlataformaMandatosUpsertCompanyRule")
            .Produces<CompanyOtMandateRuleView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPatch("/ot/{officeId:guid}/company-rules/{companyTenantId:guid}/default-signer", SetCompanyDefaultSignerAsync)
            .WithName("AdminPlataformaMandatosSetCompanyDefaultSigner")
            .Produces<CompanyOtMandateRuleView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/ot/{officeId:guid}/company-rules/{companyTenantId:guid}", DeleteCompanyRuleAsync)
            .WithName("AdminPlataformaMandatosDeleteCompanyRule")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var items = await service.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> GetAsync(
        Guid officeId,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var view = await service.GetAsync(officeId, ct).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }

    private static async Task<IResult> UpsertAsync(
        Guid officeId,
        [FromBody] UpsertMandateOtConfigRequest request,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .UpsertAsync(officeId, request, MandateEndpointHelpers.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MandateEndpointHelpers.MapWrite(status, view);
    }

    private static async Task<IResult> SetDefaultSignerAsync(
        Guid officeId,
        [FromBody] SetOtDefaultSignerRequest request,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .SetOtDefaultSignerAsync(officeId, request, MandateEndpointHelpers.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MandateEndpointHelpers.MapWrite(status, view);
    }

    private static async Task<IResult> DeleteAsync(
        Guid officeId,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var status = await service.DeleteAsync(officeId, ct).ConfigureAwait(false);
        return status == MandateConfigWriteStatus.Ok
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> UploadTemplateAsync(
        Guid officeId,
        HttpRequest http,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        if (!http.HasFormContentType)
            return Results.BadRequest(new { error = "multipart_requerido" });

        var form = await http.ReadFormAsync(ct).ConfigureAwait(false);
        var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "archivo_requerido" });

        await using var stream = file.OpenReadStream();
        var (status, view) = await service
            .UploadPdfTemplateAsync(officeId, stream, file.FileName, MandateEndpointHelpers.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MandateEndpointHelpers.MapWrite(status, view);
    }

    private static async Task<IResult> SaveEditorAsync(
        Guid officeId,
        [FromBody] SaveMandateEditorBodyRequest request,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .SaveEditorBodyAsync(officeId, request, MandateEndpointHelpers.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MandateEndpointHelpers.MapWrite(status, view);
    }

    private static async Task<IResult> DeleteTemplateAsync(
        Guid officeId,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .DeleteCustomTemplateAsync(officeId, MandateEndpointHelpers.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MandateEndpointHelpers.MapWrite(status, view);
    }

    private static IResult PreviewTemplateAsync(
        [FromRoute] string templateCode,
        [FromServices] IMandatoGenerator generator)
    {
        var code = templateCode?.Trim() ?? string.Empty;
        if (!MandatoTemplateResolver.IsRedaction(code))
            return MandateEndpointHelpers.InvalidTemplateCode();

        var doc = generator.GenerateMandato(MandatoPreviewSample.Build(code));
        return Results.File(doc.Content, contentType: "application/pdf");
    }

    private static async Task<IResult> PreviewOtAsync(
        Guid officeId,
        [FromServices] IMandateConfigAdminService service,
        [FromServices] IMandatoGenerator generator,
        CancellationToken ct)
    {
        var view = await service.GetAsync(officeId, ct).ConfigureAwait(false);
        if (view is null) return Results.NotFound();

        byte[]? customPdf = null;
        if (view.CustomTemplateKind == MandatoCustomTemplateKindCodes.Pdf)
            customPdf = await service.OpenCustomPdfAsync(officeId, ct).ConfigureAwait(false);

        var doc = generator.GenerateMandato(BuildOtPreviewData(view, customPdf));
        return Results.File(doc.Content, contentType: "application/pdf");
    }

    /// <summary>
    /// Arma la muestra de la vista previa de UN organismo. Está separado del endpoint para poder
    /// comprobar por pruebas que el organismo viaja (HU #11719): el defecto que corrige no se ve en
    /// el PDF renderizado, solo en qué datos se le pasan al generador.
    /// </summary>
    public static MandatoData BuildOtPreviewData(MandateOtConfigView view, byte[]? customPdf)
    {
        ArgumentNullException.ThrowIfNull(view);

        // HU #11719 — el organismo va explícito. Sin él, la muestra se arma con el OT CANÓNICO de la
        // plantilla (municipio ⇒ Envigado, genérico ⇒ Medellín), así que la vista previa de Bogotá
        // decía «SECRETARIA DE MOVILIDAD DE MEDELLIN … en la ciudad de Medellín». Es la única vista
        // que el administrador tiene por OT desde la HU #11705, y le mostraba otro municipio.
        // La ciudad va como marcador, igual que en el simulador: catalogs.transit_offices solo guarda
        // el código DIVIPOLA y el generador lo descarta a propósito (HU #11016).
        var sample = MandatoPreviewSample.Build(
            view.TemplateCode,
            organismo: new OrganismoTransito(view.Code, view.Name, MandatoPreviewSample.PhCiudadOrganismo));

        return sample with
        {
            InstitutionalMandataryName = view.InstitutionalMandataryName ?? sample.InstitutionalMandataryName,
            InstitutionalMandataryNit = view.InstitutionalMandataryNit ?? sample.InstitutionalMandataryNit,
            Familia = MandatoFamiliaCodes.Resolve(view.MandataryFamily),
            ChamberCity = view.ChamberCity ?? sample.ChamberCity,
            MandatarySigla = view.MandatarySigla ?? sample.MandatarySigla,
            // Abierto: bloque con líneas (Manual) y sin firmante en el sample.
            // Institucional: SinBloque (solo mandante).
            Mandatario = MandatoAssignmentModeCodes.IsOpen(view.AssignmentMode)
                ? null
                : sample.Mandatario,
            ModoFirmaMandatario = MandatoAssignmentModeCodes.IsInstitutional(view.AssignmentMode)
                ? MandatarioFirmaModo.SinBloque
                : MandatoAssignmentModeCodes.IsOpen(view.AssignmentMode)
                    ? MandatarioFirmaModo.Manual
                    : sample.ModoFirmaMandatario,
            CustomTemplateKind = view.CustomTemplateKind,
            CustomTemplateBody = view.CustomTemplateBody,
            CustomTemplatePdf = customPdf,
        };
    }

    private static async Task<IResult> ListSimulatorSignersAsync(
        Guid officeId,
        [FromServices] IMandateSimulatorService simulator,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        if (await service.GetAsync(officeId, ct).ConfigureAwait(false) is null)
            return Results.NotFound();

        var items = await simulator.ListSignersAsync(officeId, ct).ConfigureAwait(false);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> SimulatePreviewAsync(
        [FromBody] MandateSimulationRequest request,
        [FromServices] IMandateSimulatorService simulator,
        CancellationToken ct)
    {
        var result = await simulator.PreviewAsync(request, ct).ConfigureAwait(false);
        return result.Success
            ? Results.File(result.Content!, contentType: "application/pdf")
            : MapSimulation(result);
    }

    private static async Task<IResult> SimulateSendAsync(
        [FromBody] MandateSimulationSendRequest request,
        [FromServices] IMandateSimulatorService simulator,
        CancellationToken ct)
    {
        var result = await simulator.SendAsync(request, ct).ConfigureAwait(false);
        return result.Success
            ? Results.Ok(new { message = result.Message })
            : MapSimulation(result);
    }

    /// <summary>
    /// Un fallo del proveedor de correo NO es culpa de quien pidió el envío: sale como 502, no como
    /// 400. El mensaje ya viene en lenguaje de negocio desde el servicio.
    /// </summary>
    private static IResult MapSimulation(MandateSimulationResult result) => result.Outcome switch
    {
        MandateSimulationOutcome.OfficeNotFound => Results.NotFound(new { error = "ot_no_encontrado", message = result.Message }),
        MandateSimulationOutcome.SignerNotFound => Results.NotFound(new { error = "mandatario_no_encontrado", message = result.Message }),
        MandateSimulationOutcome.InvalidAssignmentMode => Results.BadRequest(new { error = "assignment_mode_invalido", message = result.Message }),
        MandateSimulationOutcome.InvalidRecipient => Results.BadRequest(new { error = "destinatario_invalido", message = result.Message }),
        MandateSimulationOutcome.SendFailed => Results.Json(
            new { error = "envio_fallido", message = result.Message },
            statusCode: StatusCodes.Status502BadGateway),
        _ => Results.BadRequest(new { error = "simulacion_invalida", message = result.Message }),
    };

    private static async Task<IResult> ListCompanyRulesAsync(
        Guid officeId,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        if (await service.GetAsync(officeId, ct).ConfigureAwait(false) is null)
            return Results.NotFound();

        // Bug #12912 — Plataforma › Mandatos es solo SuperAdmin: configura toda la red.
        var items = await service
            .ListCompanyRulesAsync(officeId, OtCompanyVisibility.WholeNetwork, ct)
            .ConfigureAwait(false);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> UpsertCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        [FromBody] UpsertCompanyOtMandateRuleRequest request,
        HttpContext http,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var change = new MandateRuleTypeChange();
        var (status, view) = await service
            .UpsertCompanyRuleAsync(officeId, companyTenantId, request, MandateEndpointHelpers.ResolveUserId(user), change, ct)
            .ConfigureAwait(false);

        // HU #13149 — bitácora del cambio de tipo (éxito con cambio real) o del intento fallido (con su código).
        if (status == MandateConfigWriteStatus.Ok)
        {
            await MandateRuleTypeAudit
                .WriteSuccessAsync(http, AuditVocabulary.Operations.Update, officeId, companyTenantId, change)
                .ConfigureAwait(false);
        }
        else
        {
            await MandateRuleTypeAudit
                .WriteFailureAsync(
                    http, AuditVocabulary.Operations.Update, officeId, companyTenantId,
                    CompanyRuleErrorCode(status), request.AssignmentMode)
                .ConfigureAwait(false);
        }

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
            // HU #13148 — otra persona cambió la regla (o falta la versión de una regla existente).
            MandateConfigWriteStatus.Conflict =>
                Results.Conflict(new { error = "row_version_conflict" }),
            _ => Results.BadRequest(),
        };
    }

    /// <summary>Código de error estable de la bitácora (el mismo que el cuerpo de la respuesta, sin datos sensibles).</summary>
    private static string CompanyRuleErrorCode(MandateConfigWriteStatus status) => status switch
    {
        MandateConfigWriteStatus.OfficeNotFound or MandateConfigWriteStatus.CompanyNotFound => "not_found",
        MandateConfigWriteStatus.InvalidAssignmentMode => "assignment_mode_invalido",
        MandateConfigWriteStatus.InvalidFamily => "mandatary_family_invalida",
        MandateConfigWriteStatus.InstitutionalRequired => "mandatario_institucional_requerido",
        MandateConfigWriteStatus.InvalidDefaultSigner => "mandatario_default_invalido",
        MandateConfigWriteStatus.Conflict => "row_version_conflict",
        _ => "bad_request",
    };

    private static async Task<IResult> SetCompanyDefaultSignerAsync(
        Guid officeId,
        Guid companyTenantId,
        [FromBody] SetCompanyDefaultSignerRequest request,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .SetCompanyDefaultSignerAsync(
                officeId, companyTenantId, request, MandateEndpointHelpers.ResolveUserId(user), OtCompanyVisibility.WholeNetwork, ct)
            .ConfigureAwait(false);

        return status switch
        {
            MandateConfigWriteStatus.Ok => Results.Ok(view),
            MandateConfigWriteStatus.OfficeNotFound or MandateConfigWriteStatus.CompanyNotFound =>
                Results.NotFound(),
            MandateConfigWriteStatus.InvalidDefaultSigner =>
                Results.BadRequest(new { error = "mandatario_default_invalido" }),
            MandateConfigWriteStatus.Conflict =>
                Results.Conflict(new { error = "row_version_conflict" }),
            _ => Results.BadRequest(),
        };
    }

    private static async Task<IResult> DeleteCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        [FromQuery] long? rowVersion,
        HttpContext http,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var change = new MandateRuleTypeChange();
        var status = await service.DeleteCompanyRuleAsync(
            officeId, companyTenantId, OtCompanyVisibility.WholeNetwork, rowVersion, change, ct).ConfigureAwait(false);

        // HU #13149 — restablecer al default queda en la bitácora con el tipo anterior.
        if (status == MandateConfigWriteStatus.Ok)
        {
            await MandateRuleTypeAudit
                .WriteSuccessAsync(http, AuditVocabulary.Operations.Delete, officeId, companyTenantId, change)
                .ConfigureAwait(false);
        }
        else
        {
            await MandateRuleTypeAudit
                .WriteFailureAsync(
                    http, AuditVocabulary.Operations.Delete, officeId, companyTenantId,
                    CompanyRuleErrorCode(status), attemptedMode: null)
                .ConfigureAwait(false);
        }

        return status switch
        {
            MandateConfigWriteStatus.Ok => Results.NoContent(),
            MandateConfigWriteStatus.Conflict => Results.Conflict(new { error = "row_version_conflict" }),
            _ => Results.NotFound(),
        };
    }
}
