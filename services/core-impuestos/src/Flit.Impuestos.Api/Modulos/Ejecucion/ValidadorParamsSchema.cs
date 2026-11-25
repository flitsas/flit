using System.Text.Json;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// Lectura del <c>params_schema</c> de una fuente (<c>impuestos.fuentes_api</c>, guía §4): los campos de <c>detail</c> que el
/// departamento exige (<c>properties.detail.required</c>). Un esquema vacío, ilegible o sin esa lista no exige nada adicional; los
/// campos de primer nivel (<c>documentType</c>, <c>documentNumber</c>, <c>plateNumber</c>) siempre van.
/// </summary>
/// <example>
/// <code>
/// var requeridos = ValidadorParamsSchema.RequeridosDetalle(fuente.ParamsSchema);
/// var falta = ValidadorParamsSchema.PrimerFaltante(requeridos, detalle); // null si está completo
/// </code>
/// </example>
public static class ValidadorParamsSchema
{
    public static IReadOnlyList<string> RequeridosDetalle(string? paramsSchema)
    {
        if (string.IsNullOrWhiteSpace(paramsSchema))
            return [];
        try
        {
            using var documento = JsonDocument.Parse(paramsSchema);
            if (documento.RootElement.ValueKind == JsonValueKind.Object
                && documento.RootElement.TryGetProperty("properties", out var propiedades) && propiedades.ValueKind == JsonValueKind.Object
                && propiedades.TryGetProperty("detail", out var detalle) && detalle.ValueKind == JsonValueKind.Object
                && detalle.TryGetProperty("required", out var requeridos) && requeridos.ValueKind == JsonValueKind.Array)
            {
                return [.. requeridos.EnumerateArray()
                    .Where(r => r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString()))
                    .Select(r => r.GetString()!.Trim())
                    .Distinct(StringComparer.Ordinal)];
            }

            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>El primer campo exigido que falta o va vacío en <paramref name="detalle"/>; null si no falta ninguno.</summary>
    public static string? PrimerFaltante(IReadOnlyList<string> requeridos, IReadOnlyDictionary<string, object?> detalle)
    {
        ArgumentNullException.ThrowIfNull(requeridos);
        ArgumentNullException.ThrowIfNull(detalle);
        return requeridos.FirstOrDefault(r => !detalle.TryGetValue(r, out var valor) || valor is null || (valor is string s && string.IsNullOrWhiteSpace(s)));
    }
}
