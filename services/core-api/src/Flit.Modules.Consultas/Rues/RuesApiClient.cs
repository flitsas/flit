using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.Documents;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Rues;

/// <summary>
/// Cliente HTTP de autogeneración del Certificado RUES (RF36), modelado sobre el flujo de FLIT v1.0
/// (<c>POST /api/v1/document-generator/rues-certificate</c> con <c>documentType=NIT</c> + número).
/// Devuelve el certificado (PDF) como Data URI/base64. Errores → <see cref="RuesExternalException"/>
/// sin incluir nunca la API key.
///
/// NOTA: el contrato de cable exacto del servicio de 2.0 (ruta, forma del cuerpo/respuesta,
/// autenticación) está PENDIENTE de confirmación del Líder Técnico; esta implementación es provisional
/// y solo se ejercita cuando <c>Rues:Enabled=true</c> (por defecto está deshabilitada y el trámite usa
/// carga manual del RUES).
/// </summary>
internal sealed class RuesApiClient(HttpClient http, IOptions<RuesOptions> options) : IRuesExternalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RuesOptions _options = options.Value;

    public async Task<RuesExternalResult> GenerarAsync(RuesExternalRequest request, CancellationToken ct = default)
    {
        var body = new RuesRequestBody(DocumentType: "NIT", DocumentNumber: request.Nit, RazonSocial: request.RazonSocial);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/document-generator/rues-certificate")
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                var transient = (int)response.StatusCode >= 500;
                throw new RuesExternalException($"El proveedor RUES respondió {(int)response.StatusCode}.", transient);
            }

            var payload = await response.Content.ReadFromJsonAsync<RuesResponseBody>(JsonOptions, ct);
            var pdf = payload?.Pdf ?? payload?.File;
            if (string.IsNullOrWhiteSpace(pdf))
                throw new RuesExternalException("Respuesta inválida del proveedor RUES.", isTransient: false);

            return new RuesExternalResult(pdf!, payload?.Radicado);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new RuesExternalException("Timeout consultando el proveedor RUES.", isTransient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new RuesExternalException($"Error de red consultando el proveedor RUES: {ex.Message}", isTransient: true);
        }
        catch (JsonException)
        {
            throw new RuesExternalException("No se pudo interpretar la respuesta del proveedor RUES.", isTransient: false);
        }
    }

    private sealed record RuesRequestBody(
        [property: JsonPropertyName("documentType")] string DocumentType,
        [property: JsonPropertyName("documentNumber")] string DocumentNumber,
        [property: JsonPropertyName("razonSocial"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RazonSocial);

    private sealed record RuesResponseBody(
        [property: JsonPropertyName("pdf")] string? Pdf,
        [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("radicado")] string? Radicado);
}
