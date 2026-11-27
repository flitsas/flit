namespace Flit.Infrastructure.KyverumRunt;

/// <summary>
/// Normaliza el tipo de documento de FLIT/Verifik (CC, CE, TI, PAS, NIT, PPT…) al código que espera
/// Kyverum RUNT — HU #10478 / ADR-0082. Los endpoints de persona y vehículo del RUNT aceptan
/// catálogos distintos:
/// <list type="bullet">
/// <item>Persona (<c>POST /v1/personas:consultar</c>): <c>C, T, E, Y, P, N</c> (más alias <c>PPT</c>
/// → <c>Y</c>, kyverum-runt #113). Enviar <c>Y</c> activa la resolución automática de identidad del
/// PPT y evita el fallback erróneo a cédula (<c>C</c>).</item>
/// <item>Vehículo (<c>POST /v1/vehiculos:consultar</c>): <c>C, E, T, P, N, D, R, PT</c>. No admite
/// <c>Y</c> ni <c>PPT</c> (respondería 400 <c>VALIDATION_ERROR</c>); omitirlo (<c>null</c>) permite
/// que Kyverum pruebe los códigos válidos de vehículo.</item>
/// </list>
/// </summary>
internal static class KyverumRuntDocType
{
    /// <summary>
    /// Normaliza el tipo de documento para <c>POST /v1/personas:consultar</c>.
    /// Mapea PPT (Permiso por Protección Temporal) al código oficial <c>"Y"</c> del catálogo RUNT.
    /// </summary>
    public static string? NormalizeForPersona(string? docType) => docType?.Trim().ToUpperInvariant() switch
    {
        "CC" or "C" => "C",
        "TI" or "T" => "T",
        "CE" or "E" => "E",
        "NIT" or "N" => "N",
        "PAS" or "PA" or "P" => "P",
        // kyverum-runt #113 — el catálogo público del RUNT persona usa "Y" para PPT y activa
        // resolución automática de identidad (Procuraduría tipo 10) + consulta RUNT cerrada.
        "PPT" or "Y" => "Y",
        _ => null,
    };

    /// <summary>
    /// Normaliza el tipo de documento del propietario para <c>POST /v1/vehiculos:consultar</c>.
    /// Para PPT devuelve <c>null</c> porque el esquema de vehículos de Kyverum RUNT no acepta
    /// <c>Y</c> ni <c>PPT</c>; omitirlo evita un rechazo <c>VALIDATION_ERROR</c>.
    /// </summary>
    public static string? NormalizeForVehicle(string? docType) => docType?.Trim().ToUpperInvariant() switch
    {
        "CC" or "C" => "C",
        "TI" or "T" => "T",
        "CE" or "E" => "E",
        "NIT" or "N" => "N",
        "PAS" or "PA" or "P" => "P",
        "PPT" or "Y" => null,
        _ => null,
    };
}
