using Flit.Api.Authorization;
using Flit.Api.Middleware;
using Flit.Tramites.Application.UseCases.ExternalAttachments;
using Flit.Tramites.Domain.ExternalSync;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13263 (Feature #13261, Épica #12741) — <c>POST /api/v1/external/tramites/{id}/adjuntos</c> (contrato v3.2 §7):
/// el cliente externo adjunta el comprobante de pago del impuesto departamental. Exige
/// <c>external.tramites.attachments.write</c>. Sin <c>X-Tenant-Id</c>: el tenant sale del trámite (ADR-0067), como en el
/// resto de rutas externas. 401 y 429 los dan los middlewares de la ruta externa (pase y cuota por cliente).
/// <para>El multipart se lee en streaming y se corta al pasar el tope: un archivo de cualquier tamaño recibe
/// <c>400 file_too_large</c> en problem+json, no el 413 genérico de Kestrel (se levanta el límite de cuerpo de ESTA ruta
/// y el tope lo aplica el propio endpoint). El archivo no se registra en ningún log ni en la bitácora de acceso.</para>
/// </summary>
public static class ExternalAttachmentEndpoints
{
    public const string EndpointName = "ExternalTramiteAdjuntoEnvio";

    private const int MaxTipoLength = 128;
    private const int MaxFileNameLength = 500;
    private const string DefaultFileName = "comprobante";

    public static IEndpointRouteBuilder MapExternalAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost($"{ExternalClientAuthorization.RoutePrefix}/tramites/{{id}}/adjuntos", UploadAsync)
            .RequireAuthorization(ExternalClientAuthorization.AttachmentsWritePolicy)
            .WithTags("External")
            .WithName(EndpointName)
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        SubmitExternalAttachmentHandler handler,
        string id,
        CancellationToken cancellationToken)
    {
        // Sin límite de cuerpo de Kestrel en esta ruta: el tope de 20 MB lo aplica la lectura (más abajo) y responde 400.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = null;
        }

        var form = await ReadFormAsync(context.Request, cancellationToken).ConfigureAwait(false);
        if (form.Error is not null)
        {
            return await Problem(context, StatusCodes.Status400BadRequest, form.Error, ErrorDetail(form.Error)).ConfigureAwait(false);
        }

        if (!Guid.TryParse(id, out var procedureId))
        {
            return await Problem(context, StatusCodes.Status404NotFound, "procedure_not_found", ErrorDetail("procedure_not_found"))
                .ConfigureAwait(false);
        }

        var result = await handler.HandleAsync(procedureId, form.Tipo, form.File, cancellationToken).ConfigureAwait(false);

        if (result.TenantId is { } tenantId)
        {
            // HU #13086 — escritura con datos personales: la compañía tocada y si se escribió; nunca el archivo.
            context.Features.Get<ExternalAccessDetails>()?.SetWrite(tenantId, result.Status == SubmitExternalAttachmentStatus.Created);
        }

        context.Response.Headers.CacheControl = "no-store";
        switch (result.Status)
        {
            case SubmitExternalAttachmentStatus.Created:
                return Results.Json(ToBody(result.Receipt!), statusCode: StatusCodes.Status201Created);
            case SubmitExternalAttachmentStatus.Unchanged:
                return Results.Json(ToBody(result.Receipt!), statusCode: StatusCodes.Status200OK);
        }

        var error = result.Error!;
        if (error == "not_allowed_in_state")
        {
            await ExternalProblem.WriteAsync(context, StatusCodes.Status409Conflict, error, ErrorDetail(error),
                result.Estado, result.Terminal, context.RequestAborted).ConfigureAwait(false);
            return Results.Empty;
        }

        var status = error switch
        {
            "procedure_not_found" => StatusCodes.Status404NotFound,
            "attachment_exists" => StatusCodes.Status409Conflict,
            "storage_unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };
        return await Problem(context, status, error, ErrorDetail(error)).ConfigureAwait(false);
    }

    private static ExternalAttachmentReceived ToBody(ExternalAttachmentReceipt r) =>
        new(r.AdjuntoId, r.Tipo, r.Sha256, r.ReemplazoDe, r.EnMatriz, r.PagadoMarcado);

    private static string ErrorDetail(string code) => code switch
    {
        "missing_file" => "Falta el archivo (parte file) o está vacío.",
        "invalid_tipo" => "tipo no es válido; la v3.2 solo acepta liquidacion_impuesto.",
        "invalid_mime" => "El Content-Type de la parte file debe ser application/pdf, image/jpeg, image/png o image/webp.",
        "file_too_large" => "El archivo excede los 20 MB permitidos.",
        "procedure_not_found" => "El trámite no existe o está fuera de alcance.",
        "not_allowed_in_state" => "El trámite está en un estado que no admite este envío.",
        "attachment_exists" => "El trámite ya tiene este documento cargado en FLIT (gestor o portal); su archivo se conserva.",
        "storage_unavailable" => "El almacenamiento de archivos no está disponible; reintentar más tarde.",
        _ => "Solicitud no válida.",
    };

    private static async Task<IResult> Problem(HttpContext context, int status, string code, string detail)
    {
        await ExternalProblem.WriteAsync(context, status, code, detail, context.RequestAborted).ConfigureAwait(false);
        return Results.Empty;
    }

    /// <summary>
    /// Lee <c>tipo</c> y <c>file</c> del multipart sin bufferizar más de lo necesario: el archivo se corta en el tope
    /// (+1 byte) y no se sigue leyendo el cuerpo. Un cuerpo que no es multipart o está mal formado es «falta el archivo».
    /// </summary>
    private static async Task<FormRead> ReadFormAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || StringSegment.IsNullOrEmpty(HeaderUtilities.RemoveQuotes(mediaType.Boundary)))
        {
            return new FormRead(null, null, "missing_file");
        }

        string? tipo = null;
        ExternalAttachmentFile? file = null;
        var tooLarge = false;
        try
        {
            var reader = new MultipartReader(HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value!, request.Body);
            while (await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false) is { } section)
            {
                var disposition = section.GetContentDispositionHeader();
                var name = disposition is null ? null : HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (name == "tipo" && tipo is null)
                {
                    tipo = await ReadTextAsync(section.Body, cancellationToken).ConfigureAwait(false);
                }
                else if (name == "file" && file is null && !tooLarge)
                {
                    var bytes = await ReadBoundedAsync(section.Body, ExternalAttachmentRules.MaxSizeBytes, cancellationToken)
                        .ConfigureAwait(false);
                    if (bytes is null)
                    {
                        tooLarge = true;
                        break; // no se lee el resto del cuerpo
                    }

                    file = new ExternalAttachmentFile(
                        FileName(disposition), section.ContentType ?? string.Empty, bytes);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            return new FormRead(null, null, "missing_file");
        }

        if (tooLarge)
        {
            // Mismo orden de validación que el resto: un tipo ya leído y no válido gana al tamaño.
            return new FormRead(tipo, null, tipo is not null && !ExternalAttachmentRules.AllowedTipos.Contains(tipo)
                ? "invalid_tipo"
                : "file_too_large");
        }

        return new FormRead(tipo, file, null);
    }

    private static async Task<string> ReadTextAsync(Stream body, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var buffer = new char[MaxTipoLength + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return new string(buffer, 0, read).Trim();
    }

    /// <summary>Todo el contenido, o <c>null</c> si pasa de <paramref name="max"/> bytes (deja de leer al pasarse).</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream body, long max, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Nombre del archivo sin ruta y acotado; es solo informativo (se muestra en el expediente).</summary>
    private static string FileName(ContentDispositionHeaderValue? disposition)
    {
        var raw = disposition is null ? null : HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
        var name = string.IsNullOrWhiteSpace(raw) ? null : Path.GetFileName(raw.Replace('\\', '/')).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return DefaultFileName;
        }

        return name.Length > MaxFileNameLength ? name[..MaxFileNameLength] : name;
    }

    private sealed record FormRead(string? Tipo, ExternalAttachmentFile? File, string? Error);
}

/// <summary>Respuesta 201/200 del envío de adjuntos (contrato v3.2 §7). <c>reemplazoDe</c> se serializa siempre, también <c>null</c>.</summary>
public sealed record ExternalAttachmentReceived(
    Guid AdjuntoId, string Tipo, string Sha256, Guid? ReemplazoDe, bool EnMatriz, bool PagadoMarcado);
