using System.Security.Claims;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Api.Authorization;
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
    /// <summary>
    /// Redacciones previsualizables por código. <c>auto</c> NO entra: no es una redacción sino una
    /// delegación en la plantilla de sistema del organismo, y sin organismo no hay nada que resolver
    /// (para eso está la vista previa por OT).
    /// </summary>
    private static readonly HashSet<string> AllowedTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        MandatoTemplateResolver.Generico,
        MandatoTemplateResolver.Sabaneta,
        MandatoTemplateResolver.Bello,
        MandatoTemplateResolver.Municipio,
    };

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

        group.MapPost("/extract", ExtractAsync)
            .WithName("AdminPlataformaMandatosExtract")
            .DisableAntiforgery()
            .Produces<MandateConfigExtractResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/ot/{officeId:guid}/company-rules", ListCompanyRulesAsync)
            .WithName("AdminPlataformaMandatosListCompanyRules")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/ot/{officeId:guid}/company-rules/{companyTenantId:guid}", UpsertCompanyRuleAsync)
            .WithName("AdminPlataformaMandatosUpsertCompanyRule")
            .Produces<CompanyOtMandateRuleView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/ot/{officeId:guid}/company-rules/{companyTenantId:guid}", DeleteCompanyRuleAsync)
            .WithName("AdminPlataformaMandatosDeleteCompanyRule")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

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
            .UpsertAsync(officeId, request, ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MapWrite(status, view);
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
            .UploadPdfTemplateAsync(officeId, stream, file.FileName, ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MapWrite(status, view);
    }

    private static async Task<IResult> SaveEditorAsync(
        Guid officeId,
        [FromBody] SaveMandateEditorBodyRequest request,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .SaveEditorBodyAsync(officeId, request, ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MapWrite(status, view);
    }

    private static async Task<IResult> DeleteTemplateAsync(
        Guid officeId,
        ClaimsPrincipal user,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .DeleteCustomTemplateAsync(officeId, ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return MapWrite(status, view);
    }

    private static IResult PreviewTemplateAsync(
        [FromRoute] string templateCode,
        [FromServices] IMandatoGenerator generator)
    {
        var code = templateCode?.Trim() ?? string.Empty;
        if (!AllowedTemplates.Contains(code))
        {
            return Results.Json(
                new { error = "template_code_invalido", allowed = AllowedTemplates.OrderBy(x => x) },
                statusCode: StatusCodes.Status400BadRequest);
        }

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

    private static async Task<IResult> ExtractAsync(
        HttpRequest http,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        if (!http.HasFormContentType)
            return Results.BadRequest(new { error = "multipart_requerido" });

        var form = await http.ReadFormAsync(ct).ConfigureAwait(false);
        var file = form.Files.GetFile("file");
        if (file is null && form.Files.Count > 0)
            file = form.Files[0];
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "archivo_requerido" });

        if (file.Length > 10 * 1024 * 1024)
            return Results.BadRequest(new { error = "archivo_demasiado_grande" });

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);

        var mediaType = file.ContentType?.Trim() ?? "application/octet-stream";
        if (string.IsNullOrWhiteSpace(mediaType) || mediaType == "application/octet-stream")
        {
            mediaType = file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : "image/png";
        }

        var result = await service.ExtractAsync(ms.ToArray(), mediaType, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListCompanyRulesAsync(
        Guid officeId,
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
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
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
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
        [FromServices] IMandateConfigAdminService service,
        CancellationToken ct)
    {
        var status = await service.DeleteCompanyRuleAsync(officeId, companyTenantId, ct).ConfigureAwait(false);
        return status == MandateConfigWriteStatus.Ok
            ? Results.NoContent()
            : Results.NotFound();
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
            MandateConfigWriteStatus.InvalidTemplateFile =>
                Results.BadRequest(new { error = "plantilla_pdf_invalida" }),
            MandateConfigWriteStatus.InvalidEditorBody =>
                Results.BadRequest(new { error = "editor_cuerpo_invalido" }),
            _ => Results.BadRequest(),
        };

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
