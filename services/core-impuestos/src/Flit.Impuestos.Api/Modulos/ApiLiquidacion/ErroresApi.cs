using System.Text.Json;

namespace Flit.Impuestos.Api.Modulos.ApiLiquidacion;

/// <summary>
/// Códigos de <c>error.code</c> del API de liquidación (guía de integración §3, §5 y §8) y lectura del cuerpo de error, que
/// siempre tiene la forma <c>{"error": {"code": "...", "message": "..."}}</c>. Solo se conserva el código: el mensaje puede
/// repetir datos de la petición y no se guarda ni se registra.
/// </summary>
/// <example>
/// <code>
/// ErroresApi.LeerCodigo("{\"error\":{\"code\":\"tax_id_taken\",\"message\":\"…\"}}"); // "tax_id_taken"
/// ErroresApi.LeerCodigo("&lt;html&gt;");                                                   // null
/// </code>
/// </example>
public static class ErroresApi
{
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string NotFound = "not_found";
    public const string TaxIdTaken = "tax_id_taken";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string SourceInactive = "source_inactive";
    public const string DepartmentUnsupported = "department_unsupported";
    public const string InvalidParams = "invalid_params";
    public const string AdapterUnavailable = "adapter_unavailable";
    public const string WebhookNotConfigured = "webhook_not_configured";
    public const string FileMissing = "file_missing";
    public const string StorageUnavailable = "storage_unavailable";

    /// <summary>Largo máximo de un código que se acepta (los del contrato son cortos; más largo no es un código).</summary>
    public const int LargoMaximoCodigo = 64;

    /// <summary>El <c>error.code</c> del cuerpo; null si el cuerpo está vacío, no es JSON o no trae el código.</summary>
    public static string? LeerCodigo(string? cuerpo)
    {
        if (string.IsNullOrWhiteSpace(cuerpo))
            return null;
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            if (documento.RootElement.ValueKind == JsonValueKind.Object
                && documento.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var codigo)
                && codigo.ValueKind == JsonValueKind.String)
            {
                var valor = codigo.GetString();
                return string.IsNullOrWhiteSpace(valor) || valor.Length > LargoMaximoCodigo ? null : valor;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
