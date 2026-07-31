using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.DataMigration.V1.Storage;

/// <summary>Endpoint del API de V1 que materializa los documentos de un trámite.</summary>
public sealed class V1SnapshotEndpoint
{
    /// <summary>Base del API de traspasos de V1 (p. ej. <c>https://api-transfer.flit.../</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Ruta del recurso, sin el id.</summary>
    public string Path { get; set; } = "api/v1/vehicle-transfer-migration";

    /// <summary>Token de acceso de V1. El endpoint exige autenticación.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>Qué piezas pedir: <c>generated</c> (por defecto) o <c>all</c>.</summary>
    public string Include { get; set; } = "generated";

    /// <summary>Qué hacer con el consolidado: <c>auto</c> (por defecto), <c>always</c> o <c>never</c>.</summary>
    public string Consolidated { get; set; } = "auto";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>Una pieza del expediente materializada por V1.</summary>
public sealed record V1SnapshotPiece(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("documentTypeCode")] string DocumentTypeCode,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("sourceFileId")] string? SourceFileId,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("contentBase64")] string ContentBase64);

/// <summary>Pieza ausente o entregada con menor fidelidad, con su motivo.</summary>
public sealed record V1SnapshotIssue(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>Consolidados que V1 sí tenía persistidos al momento del snapshot.</summary>
public sealed record V1PersistedConsolidated(
    [property: JsonPropertyName("preparedFileId")] string? PreparedFileId,
    [property: JsonPropertyName("draftFileId")] string? DraftFileId);

/// <summary>Respuesta completa del endpoint de snapshot.</summary>
public sealed record V1Snapshot(
    [property: JsonPropertyName("transferId")] long TransferId,
    [property: JsonPropertyName("plate")] string? Plate,
    [property: JsonPropertyName("statusId")] int StatusId,
    [property: JsonPropertyName("statusName")] string StatusName,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("persistedConsolidated")] V1PersistedConsolidated PersistedConsolidated,
    [property: JsonPropertyName("pieces")] IReadOnlyList<V1SnapshotPiece> Pieces,
    [property: JsonPropertyName("issues")] IReadOnlyList<V1SnapshotIssue> Issues);

/// <summary>
/// Pide a V1 que materialice el expediente de un trámite.
/// <para>
/// V1 arma sus documentos en caliente y no los guarda: la portada, el FUR, la compraventa del
/// sistema, las cartas selfie, el mandato y demás no existen como archivo en ninguna parte. Este
/// cliente los trae UNA vez, en el momento de migrar, para que V2 los persista antes de que V1 se
/// apague. La llamada es de solo lectura: V1 no se modifica.
/// </para>
/// </summary>
public sealed class V1SnapshotClient(HttpClient http, V1SnapshotEndpoint endpoint)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Devuelve el snapshot del trámite, o <c>null</c> si V1 no lo conoce (404).
    /// Los binarios pesan, así que el timeout del <see cref="HttpClient"/> debe ser generoso.
    /// </summary>
    public async Task<V1Snapshot?> GetAsync(long v1Id, CancellationToken cancellationToken)
    {
        var path = endpoint.Path.Trim('/');
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{path}/{v1Id}/snapshot?include={endpoint.Include}&consolidated={endpoint.Consolidated}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(endpoint.AuthToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.AuthToken);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"V1 respondió {(int)response.StatusCode} al pedir el snapshot del trámite {v1Id}: {Truncate(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<V1Snapshot>(stream, JsonOptions, cancellationToken);
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";
}
