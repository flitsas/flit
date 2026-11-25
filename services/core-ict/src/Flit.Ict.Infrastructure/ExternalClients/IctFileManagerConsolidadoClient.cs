using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Grpc.Contracts;
using Flit.Ict.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// HU #14668 — URL prefirmada del consolidado:
/// <list type="number">
///   <item>gRPC <c>DeliverConsolidado</c> en core-api (regenera on-demand vía EntregarConsolidadoHandler).</item>
///   <item>File Manager <c>GET …/presigned-url?ttl=600</c> con el <c>storage_path</c> recibido.</item>
/// </list>
/// </summary>
public sealed partial class IctFileManagerConsolidadoClient(
    HttpClient http,
    IctOrchestration.IctOrchestrationClient orchestration,
    IOptions<FileManagerOptions> options,
    ILogger<IctFileManagerConsolidadoClient> logger) : IIctConsolidadoClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _fm = options.Value;

    public async Task<(string? Url, string? Error)> GetPresignedUrlAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        CancellationToken ct = default)
    {
        // 1. Regenerar/entregar consolidado en core-api (on-demand si falta o está stale).
        DeliverConsolidadoReply deliver;
        try
        {
            deliver = await orchestration.DeliverConsolidadoAsync(
                new DeliverConsolidadoRequest
                {
                    TenantId = tenantId.ToString(),
                    ProcedureInstanceId = procedureInstanceId.ToString(),
                },
                cancellationToken: ct);
        }
#pragma warning disable CA1031 // gRPC caído → 503 storage_unavailable (no reventar el endpoint ICT)
        catch (Exception ex)
        {
            Log.DeliverFailed(logger, procedureInstanceId, ex);
            return (null, "storage_unavailable");
        }
#pragma warning restore CA1031

        if (!string.IsNullOrWhiteSpace(deliver.ErrorCode))
        {
            return (null, deliver.ErrorCode);
        }

        if (string.IsNullOrWhiteSpace(deliver.StoragePath))
        {
            return (null, "consolidado_no_disponible");
        }

        // 2. Presigned GET URL (TTL 600s).
        var url = await GetPresignedUrlFromFileManagerAsync(deliver.StoragePath, ct);
        return string.IsNullOrWhiteSpace(url)
            ? (null, "storage_unavailable")
            : (url, null);
    }

    /// <summary>
    /// Llama al File Manager: <c>GET {filesPath}/{storageId}/presigned-url?disposition=inline&amp;ttl=600</c>.
    /// </summary>
    private async Task<string?> GetPresignedUrlFromFileManagerAsync(string storageId, CancellationToken ct)
    {
        if (http.BaseAddress is null)
        {
            return null;
        }

        var filesPath = string.IsNullOrWhiteSpace(_fm.FilesPath) ? "api/v1/files" : _fm.FilesPath;
        var path = $"{filesPath}/{Uri.EscapeDataString(storageId)}/presigned-url?disposition=inline&ttl=600";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req);

        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<FilePresignedResponse>(JsonOpts, ct);
            return body?.PresignedUrl?.Url;
        }
#pragma warning disable CA1031 // errores de red o formato inesperado → null (503 en el endpoint)
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_fm.AuthToken))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _fm.AuthToken);
        }
    }

    private sealed record FilePresignedResponse(
        [property: JsonPropertyName("presignedUrl")] PresignedUrlDto? PresignedUrl);

    private sealed record PresignedUrlDto(
        [property: JsonPropertyName("url")] string? Url);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "ICT consolidado: gRPC DeliverConsolidado falló para {InstanceId}")]
        public static partial void DeliverFailed(ILogger logger, Guid instanceId, Exception ex);
    }
}
