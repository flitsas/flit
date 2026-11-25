using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Impuestos.Api.Modulos.ApiLiquidacion;

// Formas del API de liquidación (guía de integración §3–§8). Los nombres JSON son los del contrato, exactos. Todo tipo que
// pueda llevar un secreto o PII redefine ToString para no imprimirlo (EP-CF3: un log de un record no filtra valores).

/// <summary>
/// Cuerpo EXACTO del alta (<c>POST /api/v1/integracion/empresas</c>, guía §3): <c>legal_name</c>, <c>tax_id</c> y
/// <c>contact_email</c>, sin campos adicionales.
/// </summary>
public sealed record AltaEmpresaPeticion(
    [property: JsonPropertyName("legal_name")] string LegalName,
    [property: JsonPropertyName("tax_id")] string TaxId,
    [property: JsonPropertyName("contact_email")] string ContactEmail)
{
    public override string ToString() => $"AltaEmpresaPeticion {{ TaxId = {TaxId}, LegalName = [{LegalName.Length} car.], ContactEmail = [REDACTADO] }}";
}

/// <summary>Empresa del API (<c>company</c> del alta). Sin secretos: <c>api_key_prefix</c> no lo es.</summary>
public sealed record EmpresaApi(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tax_id")] string? TaxId,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("api_key_prefix")] string? ApiKeyPrefix,
    [property: JsonPropertyName("has_webhook_secret")] bool? HasWebhookSecret)
{
    /// <summary>Dígito de verificación que calculó el proveedor (guía §3); FLIT envía el NIT base (D-TAXID).</summary>
    [JsonPropertyName("tax_id_dv")]
    public int? TaxIdDv { get; init; }
}

/// <summary>
/// 200 o 201 del alta idempotente (guía §3, 2026-11-25): la empresa, <c>created</c> (201 = nació en esta llamada, 200 = ya
/// existía), la <c>api_key</c> y el <c>webhook_secret</c> (vienen SIEMPRE, los mismos en cada llamada con ese NIT) y
/// <c>api_key_rotated</c> (true una sola vez por empresa: la llave anterior dejó de servir).
/// </summary>
public sealed record AltaEmpresaRespuesta(
    [property: JsonPropertyName("company")] EmpresaApi Company,
    [property: JsonPropertyName("api_key")] string ApiKey,
    [property: JsonPropertyName("webhook_secret")] string? WebhookSecret)
{
    [JsonPropertyName("created")]
    public bool? Created { get; init; }

    [JsonPropertyName("api_key_rotated")]
    public bool? ApiKeyRotated { get; init; }

    public override string ToString() =>
        $"AltaEmpresaRespuesta {{ Company = {Company.Id}, Created = {Created}, ApiKeyRotated = {ApiKeyRotated}, ApiKey = [REDACTADO], WebhookSecret = [REDACTADO] }}";
}

/// <summary>Una fuente (departamento) de <c>GET /api/v1/fuentes</c> (guía §4).</summary>
public sealed record FuenteDelApi(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("department_code")] string DepartmentCode,
    [property: JsonPropertyName("department_name")] string? DepartmentName,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("params_schema")] JsonElement? ParamsSchema);

/// <summary><c>GET /api/v1/fuentes</c>: <c>{ items: [...] }</c>.</summary>
public sealed record FuentesRespuesta([property: JsonPropertyName("items")] IReadOnlyList<FuenteDelApi> Items);

/// <summary>
/// Cuerpo de <c>POST /api/v1/consultas</c> (guía §5): solo los campos del contrato. <c>callbackUrl</c> no se envía si es null.
/// Los campos del contrato anterior (<see cref="CamposProhibidos"/>) no existen aquí y el cliente los rechaza dentro de
/// <c>detail</c>. Lleva PII (documento, propietario): no se registra.
/// </summary>
public sealed record SolicitudConsulta(
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("documentNumber")] string DocumentNumber,
    [property: JsonPropertyName("plateNumber")] string PlateNumber,
    [property: JsonPropertyName("callbackUrl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CallbackUrl,
    [property: JsonPropertyName("detail")] IReadOnlyDictionary<string, object?> Detail)
{
    /// <summary>Campos que el API ya no acepta (responden 400).</summary>
    public static IReadOnlyList<string> CamposProhibidos { get; } = ["processStep", "lastTaxHandler", "credentials"];

    public override string ToString() => $"SolicitudConsulta {{ [REDACTADO: PII], CallbackUrl = {(CallbackUrl is null ? "no" : "sí")}, Detail = {Detail.Count} campos }}";
}

/// <summary>202 de <c>POST /api/v1/consultas</c>.</summary>
public sealed record ConsultaCreada(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("source_id")] string? SourceId);

/// <summary>
/// Una consulta de <c>GET /api/v1/consultas/{id}</c> o del listado (guía §7): los campos que el servicio usa para correlacionar
/// y <see cref="CuerpoCrudo"/>, el JSON tal cual llegó, para quien lo procese o lo guarde cifrado. No se registra.
/// </summary>
public sealed record ConsultaApi(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("attempt")] int? Attempt,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("callback_url")] string? CallbackUrl)
{
    [JsonIgnore]
    public string CuerpoCrudo { get; init; } = string.Empty;

    public override string ToString() => $"ConsultaApi {{ Id = {Id}, Status = {Status}, Attempt = {Attempt}, ErrorCode = {ErrorCode} }}";
}

/// <summary><c>GET /api/v1/consultas?status=&amp;limit=&amp;cursor=</c>: una página y el cursor de la siguiente.</summary>
public sealed record ListadoConsultas(
    [property: JsonPropertyName("items")] IReadOnlyList<ConsultaApi> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

/// <summary>Un PDF de <c>GET /api/v1/files/{fileId}/download</c> (guía §8).</summary>
public sealed record ArchivoApi(byte[] Contenido, string? TipoContenido, string? NombreArchivo)
{
    public override string ToString() => $"ArchivoApi {{ {Contenido.Length} bytes, {TipoContenido} }}";
}
