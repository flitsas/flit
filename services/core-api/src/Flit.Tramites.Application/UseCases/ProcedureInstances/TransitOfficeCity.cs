namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Ciudad del organismo de tránsito para los documentos (HU #11016). El field_value
/// <c>transit_office_city</c> guarda el CÓDIGO DIVIPOLA del municipio (p. ej. «25286»), no su nombre.
/// <para>El camino preferido para imprimir la ciudad legible es
/// <c>transit_office_city_name</c> / <c>transit_office_actual_city_name</c>, hidratados desde
/// <c>catalogs.transit_offices.city_name</c>. Si solo hay código DIVIPOLA, <see cref="Legible"/> lo
/// descarta y <see cref="ForDocuments"/> intenta inferir el municipio desde el nombre del OT.</para>
/// </summary>
public static class TransitOfficeCity
{
    /// <summary>Nombre legible de la ciudad, o <c>null</c> si el valor es un código o está vacío.</summary>
    public static string? Legible(string? ciudad)
    {
        var valor = ciudad?.Trim();
        if (string.IsNullOrEmpty(valor))
            return null;

        return valor.All(char.IsDigit) ? null : valor;
    }

    /// <summary>
    /// Ciudad para documentos: nombre en field_value si es legible; si no, inferida del nombre del OT.
    /// </summary>
    public static string? ForDocuments(string? ciudad, string? organismoNombre)
    {
        var legible = Legible(ciudad);
        if (legible is not null)
            return legible;

        return InferFromOrganismoName(organismoNombre);
    }

    /// <summary>
    /// Extrae el municipio del nombre del organismo cuando el field_value solo trae DIVIPOLA.
    /// Patrones frecuentes en el catálogo: «… de {Ciudad}» y «{Ciudad} — …».
    /// </summary>
    internal static string? InferFromOrganismoName(string? organismoNombre)
    {
        var nombre = organismoNombre?.Trim();
        if (string.IsNullOrEmpty(nombre))
            return null;

        var dashIdx = nombre.IndexOf('—');
        if (dashIdx < 0)
            dashIdx = nombre.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 0)
        {
            var left = nombre[..dashIdx].Trim();
            if (IsPlausibleCityToken(left))
                return left;
        }

        var deIdx = nombre.LastIndexOf(" de ", StringComparison.OrdinalIgnoreCase);
        if (deIdx >= 0)
        {
            var tail = nombre[(deIdx + 4)..].Trim();
            if (IsPlausibleCityToken(tail))
                return tail;
        }

        return null;
    }

    private static bool IsPlausibleCityToken(string token)
    {
        if (token.Length is 0 or > 40)
            return false;
        if (token.All(char.IsDigit))
            return false;

        // Títulos de secretaría: no son ciudad.
        return token.IndexOf("SECRETARIA", StringComparison.OrdinalIgnoreCase) < 0
            && token.IndexOf("SECRETARÍA", StringComparison.OrdinalIgnoreCase) < 0
            && token.IndexOf("TRANSITO", StringComparison.OrdinalIgnoreCase) < 0
            && token.IndexOf("TRÁNSITO", StringComparison.OrdinalIgnoreCase) < 0
            && token.IndexOf("MOVILIDAD", StringComparison.OrdinalIgnoreCase) < 0
            && token.IndexOf("STRIA", StringComparison.OrdinalIgnoreCase) < 0
            && token.IndexOf("TTEyTTO", StringComparison.OrdinalIgnoreCase) < 0
            && token.IndexOf("STTMP", StringComparison.OrdinalIgnoreCase) < 0;
    }
}
