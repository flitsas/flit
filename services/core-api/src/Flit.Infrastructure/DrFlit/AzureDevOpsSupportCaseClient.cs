using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using Flit.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Radica los casos de DR. FLIT como Bug en Azure DevOps con la REST API cruda (HU #12923, ADR-0060 §7.2).
/// Sin SDK: typed <see cref="HttpClient"/>, PAT en Basic auth, JSON Patch.
/// <list type="number">
///   <item>Sube cada adjunto a <c>_apis/wit/attachments</c>. Si uno falla se excluye y se cuenta (AC3).</item>
///   <item>Crea el Bug en una sola llamada con los campos mapeados por <see cref="DrFlitFieldMappingOptions"/>
///   y las relaciones <c>AttachedFile</c> de los que sí subieron, asignado a la cuenta de soporte configurada.</item>
/// </list>
/// Reintenta 1 vez ante fallo de transporte, timeout o 5xx. Logs sin PII: solo códigos y el id del work item.
/// </summary>
internal sealed class AzureDevOpsSupportCaseClient(
    HttpClient http,
    IOptions<AzureDevOpsOptions> options,
    IOptions<DrFlitFieldMappingOptions> mapping,
    ILogger<AzureDevOpsSupportCaseClient> logger) : IDrFlitSupportCaseGateway
{
    private const string ApiVersion = "7.1";
    private const int MaxAttempts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AzureDevOpsOptions _options = options.Value;
    private readonly DrFlitFieldMappingOptions _mapping = mapping.Value;

    public async Task<DrFlitBugCreationResult> CreateBugAsync(
        DrFlitSupportTicket ticket,
        IReadOnlyList<DrFlitTicketAttachment> attachments,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(attachments);

        if (string.IsNullOrWhiteSpace(_options.Pat) || string.IsNullOrWhiteSpace(_options.OrganizationUrl))
        {
            AzureDevOpsLog.NotConfigured(logger);
            return DrFlitBugCreationResult.Failed("not_configured");
        }

        var uploaded = new List<(string Url, string FileName)>();
        var failed = 0;
        foreach (var attachment in attachments)
        {
            var url = await UploadAttachmentAsync(attachment, ct).ConfigureAwait(false);
            if (url is null)
                failed++;
            else
                uploaded.Add((url, attachment.FileName));
        }

        var patch = BuildPatch(ticket, uploaded);
        var (status, body, error) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, ProjectUrl("_apis/wit/workitems/$Bug"))
            {
                Content = new StringContent(JsonSerializer.Serialize(patch, JsonOptions), Encoding.UTF8, "application/json-patch+json"),
            },
            ct).ConfigureAwait(false);

        if (error is not null || status != HttpStatusCode.OK || body is null)
        {
            var code = error ?? $"http_{(int)status}";
            AzureDevOpsLog.CreateFailed(logger, code);
            return DrFlitBugCreationResult.Failed(code, failed);
        }

        var created = JsonSerializer.Deserialize<WorkItemResponse>(body, JsonOptions);
        if (created?.Id is not > 0)
        {
            AzureDevOpsLog.CreateFailed(logger, "invalid_response");
            return DrFlitBugCreationResult.Failed("invalid_response", failed);
        }

        AzureDevOpsLog.Created(logger, created.Id.Value, uploaded.Count, failed);
        return new DrFlitBugCreationResult(true, created.Id, WorkItemWebUrl(created.Id.Value), failed, null);
    }

    /// <summary>
    /// JSON Patch del Bug. Todo valor del usuario va HTML-encoded dentro de <c>ReproSteps</c>/<c>Description</c>
    /// (son campos HTML en ADO). Los campos de clasificación salen del mapeo configurado, nunca de constantes.
    /// </summary>
    internal List<PatchOperation> BuildPatch(DrFlitSupportTicket ticket, IReadOnlyList<(string Url, string FileName)> uploaded)
    {
        var html = BuildReproStepsHtml(ticket, uploaded.Select(u => u.FileName).ToList());
        var ops = new List<PatchOperation>
        {
            Add("System.Title", Truncate($"{_options.TitlePrefix} {ticket.Title.Trim()}", 255)),
            Add("Microsoft.VSTS.TCM.ReproSteps", html),
            Add("System.Description", html),
            Add("Custom.AffectedModule", ResolveAffectedModule(ticket.AffectedModule)),
            Add("Custom.TypeBug", _mapping.TypeBug),
        };

        // Asignado a la cuenta de soporte para que entre directo a su cola; con la opción vacía se
        // conserva el comportamiento anterior (sin asignar, soporte triagea).
        if (!string.IsNullOrWhiteSpace(_options.AssignedTo))
            ops.Add(Add("System.AssignedTo", _options.AssignedTo.Trim()));

        AddMapped(ops, "Custom.Primacy", _mapping.Primacy, ticket.Priority.ToString());
        AddMapped(ops, "Microsoft.VSTS.Common.Severity", _mapping.Severity, ticket.Priority.ToString());
        AddMapped(ops, "Custom.Incidence", _mapping.Incidence, ticket.Frequency.ToWire());
        AddMapped(ops, "Custom.Environment", _mapping.Environment, ticket.Environment.ToString());

        foreach (var (url, _) in uploaded)
        {
            ops.Add(new PatchOperation("add", "/relations/-", new AttachmentRelation("AttachedFile", url)));
        }

        return ops;
    }

    public string DestinationName => _options.Project;

    /// <summary>AC4 — módulo del allow-list configurado, o el default configurado.</summary>
    public string ResolveAffectedModule(string? requested)
    {
        var match = _mapping.AffectedModules.FirstOrDefault(m =>
            string.Equals(m, requested?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? _mapping.DefaultAffectedModule;
    }

    /// <summary>
    /// Mismas etiquetas y orden que el formulario web de soporte, para que quien triagea lea igual un caso
    /// venga de donde venga. La fecha va en hora Colombia.
    /// </summary>
    internal static string BuildReproStepsHtml(DrFlitSupportTicket t, IReadOnlyList<string> attachmentNames)
    {
        // Escape mínimo (& < > " '): neutraliza HTML del usuario sin convertir tildes ni eñes en
        // entidades numéricas, que WebUtility.HtmlEncode sí convierte y ensucian el texto en ADO.
        static string E(string? s) => (s ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
        static string Multiline(string s) => E(s.Trim()).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);

        var fecha = TimeZoneInfo.ConvertTime(t.ReportedAt, BogotaDays.Zone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var frecuencia = t.Frequency switch
        {
            DrFlitCaseFrequency.UnaVez => "Una vez",
            DrFlitCaseFrequency.AVeces => "A Veces",
            _ => "Siempre",
        };
        var ambiente = t.Environment switch
        {
            DrFlitDeployEnvironment.PDN => "Producción",
            DrFlitDeployEnvironment.QA => "QA",
            _ => "Desarrollo",
        };
        var adjuntos = attachmentNames.Count == 0 ? "Sin adjuntos" : string.Join(", ", attachmentNames.Select(E));

        var sb = new StringBuilder("<html><head></head><body><ol>");
        void Li(string label, string valueHtml) => sb.Append("<li>").Append(label).Append(": ").Append(valueHtml).Append("</li>");
        Li("Nombre", E(t.RequesterName));
        Li("email del usuario", E(t.RequesterEmail));
        Li("compañía", E(t.Company));
        Li("fecha", fecha);
        Li("Detalle del Error", Multiline(t.Detail));
        Li("Resultado Esperado", Multiline(t.ExpectedResult));
        Li("Adjuntos", adjuntos);
        Li("Frecuencia del Error", frecuencia);
        Li("Ambiente", ambiente);
        Li("Teléfono", E(t.RequesterPhone));
        Li("Título", E(t.Title));
        Li("Prioridad", t.Priority.ToString());
        Li("Correo", $"<a href=\"mailto:{E(t.RequesterEmail)}\">{E(t.RequesterEmail)}</a>");
        Li("Origen", "DR. FLIT (chat de la plataforma)");
        sb.Append("</ol></body></html>");
        return sb.ToString();
    }

    private async Task<string?> UploadAttachmentAsync(DrFlitTicketAttachment attachment, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            var stream = await attachment.OpenAsync(ct).ConfigureAwait(false);
            if (stream is null)
            {
                AzureDevOpsLog.AttachmentFailed(logger, "missing_content");
                return null;
            }

            await using (stream.ConfigureAwait(false))
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                bytes = ms.ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException)
        {
            AzureDevOpsLog.AttachmentFailed(logger, "read_failed");
            return null;
        }

        var (status, body, error) = await SendAsync(
            () =>
            {
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return new HttpRequestMessage(
                    HttpMethod.Post,
                    ProjectUrl($"_apis/wit/attachments?fileName={Uri.EscapeDataString(attachment.FileName)}"))
                {
                    Content = content,
                };
            },
            ct).ConfigureAwait(false);

        if (error is not null || status is not (HttpStatusCode.OK or HttpStatusCode.Created) || body is null)
        {
            AzureDevOpsLog.AttachmentFailed(logger, error ?? $"http_{(int)status}");
            return null;
        }

        var reference = JsonSerializer.Deserialize<AttachmentResponse>(body, JsonOptions);
        return string.IsNullOrWhiteSpace(reference?.Url) ? null : reference.Url;
    }

    /// <summary>
    /// Envía con 1 reintento ante transporte, timeout o 5xx. Una respuesta 4xx no se reintenta: es un
    /// error de datos o permisos que no cambia al repetir.
    /// </summary>
    private async Task<(HttpStatusCode Status, string? Body, string? Error)> SendAsync(
        Func<HttpRequestMessage> build, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var last = attempt == MaxAttempts;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

                using var request = build();
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + _options.Pat)));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if ((int)response.StatusCode >= 500 && !last)
                {
                    AzureDevOpsLog.RetryingHttp(logger, attempt, (int)response.StatusCode);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                return (response.StatusCode, body, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (last)
                    return (default, null, "timeout");
                AzureDevOpsLog.Retrying(logger, attempt, "timeout");
            }
            catch (HttpRequestException)
            {
                if (last)
                    return (default, null, "network");
                AzureDevOpsLog.Retrying(logger, attempt, "network");
            }
        }

        return (default, null, "network");
    }

    /// <summary>
    /// URL absoluta bajo el proyecto. El nombre del proyecto lleva espacios (<c>FLIT - SOPORTE</c>) y se
    /// codifica como segmento de ruta (AC2); <c>$Bug</c> va literal, que es como lo espera la API.
    /// </summary>
    internal Uri ProjectUrl(string relative) =>
        new($"{_options.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(_options.Project)}/{relative}"
            + (relative.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "api-version=" + ApiVersion);

    private string WorkItemWebUrl(int id) =>
        $"{_options.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(_options.Project)}/_workitems/edit/{id}";

    private static PatchOperation Add(string field, string value) => new("add", "/fields/" + field, value);

    private static void AddMapped(List<PatchOperation> ops, string field, Dictionary<string, string> map, string key)
    {
        // Sin mapeo configurado para esa clave el campo no se envía: mejor un campo vacío que soporte
        // completa al triagear que un valor inventado que el picklist de ADO rechace con 400.
        if (map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            ops.Add(Add(field, value));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    internal sealed record PatchOperation(
        [property: JsonPropertyName("op")] string Op,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("value")] object Value);

    internal sealed record AttachmentRelation(
        [property: JsonPropertyName("rel")] string Rel,
        [property: JsonPropertyName("url")] string Url);

    private sealed record WorkItemResponse([property: JsonPropertyName("id")] int? Id);

    private sealed record AttachmentResponse([property: JsonPropertyName("url")] string? Url);
}

/// <summary>Logging source-generated (CA1848). Nunca PAT, nunca datos del caso.</summary>
internal static partial class AzureDevOpsLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "DR. FLIT soporte: Azure DevOps sin configurar (falta PAT u organización); no se radica el caso")]
    public static partial void NotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "DR. FLIT soporte: caso creado como work item {WorkItemId} ({Uploaded} adjuntos, {Failed} fallidos)")]
    public static partial void Created(ILogger logger, int workItemId, int uploaded, int failed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DR. FLIT soporte: no se pudo crear el caso en Azure DevOps ({ErrorCode})")]
    public static partial void CreateFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DR. FLIT soporte: un adjunto no se pudo subir ({ErrorCode}); el caso sigue sin él")]
    public static partial void AttachmentFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "DR. FLIT soporte: reintentando Azure DevOps tras {ErrorCode} (intento {Attempt})")]
    public static partial void Retrying(ILogger logger, int attempt, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "DR. FLIT soporte: reintentando Azure DevOps tras HTTP {StatusCode} (intento {Attempt})")]
    public static partial void RetryingHttp(ILogger logger, int attempt, int statusCode);
}
