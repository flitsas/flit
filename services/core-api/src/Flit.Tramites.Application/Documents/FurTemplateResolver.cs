using System.Text;

namespace Flit.Tramites.Application.Documents;

/// <summary>Plantilla de FUR a generar según la clasificación del vehículo (HU #10919, Feature #10918).</summary>
public enum FurTemplateFormat
{
    /// <summary>Registro Nacional Automotor (formato por defecto).</summary>
    Automotor,

    /// <summary>Registro Nacional de Maquinaria Agrícola y de Construcción Autopropulsada.</summary>
    Maquinaria,

    /// <summary>Registro Nacional de Remolques, Semirremolques, Multimodulares y Similares.</summary>
    Remolques,
}

/// <summary>
/// Resuelve la plantilla de FUR a partir de la clasificación del vehículo (<c>vehicle_class</c>) usando el
/// catálogo <c>tramites.vehicle_classification_fur</c> (HU #10919). El backend es la fuente de verdad (D1);
/// una clasificación sin match en el catálogo cae a <see cref="FurTemplateFormat.Automotor"/> (D2).
/// </summary>
public interface IFurTemplateResolver
{
    Task<FurTemplateFormat> ResolveAsync(string? vehicleClass, CancellationToken ct = default);
}

/// <summary>
/// Normaliza una clasificación para compararla contra el catálogo (HU #10919): mayúsculas, sin
/// diacríticos/tildes y con los espacios internos colapsados y recortados. Pura (unit-testeable).
/// El plegado de acentos se hace por mapeo explícito (no vía <c>String.Normalize</c>), porque el runtime
/// corre en modo globalization-invariant sin ICU y ahí la normalización Unicode no decompone.
/// </summary>
public static class FurClassificationNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var upper = value.Trim().ToUpperInvariant();
        var collapsed = string.Join(' ', upper.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var sb = new StringBuilder(collapsed.Length);
        foreach (var ch in collapsed)
        {
            sb.Append(ch switch
            {
                'Á' or 'À' or 'Ä' or 'Â' or 'Ã' => 'A',
                'É' or 'È' or 'Ë' or 'Ê' => 'E',
                'Í' or 'Ì' or 'Ï' or 'Î' => 'I',
                'Ó' or 'Ò' or 'Ö' or 'Ô' or 'Õ' => 'O',
                'Ú' or 'Ù' or 'Ü' or 'Û' => 'U',
                'Ñ' => 'N',
                'Ç' => 'C',
                _ => ch,
            });
        }
        return sb.ToString();
    }
}

/// <summary>
/// Lógica pura de resolución clasificación → plantilla (HU #10919), separada del acceso a datos para poder
/// unit-testear sin BD. El catálogo se recibe ya normalizado (<c>clave = clasificación normalizada</c>).
/// </summary>
public static class FurTemplateResolution
{
    /// <summary>Resuelve la plantilla; sin match en el catálogo → <see cref="FurTemplateFormat.Automotor"/> (D2).</summary>
    public static FurTemplateFormat Resolve(string? vehicleClass, IReadOnlyDictionary<string, FurTemplateFormat> normalizedCatalog)
    {
        ArgumentNullException.ThrowIfNull(normalizedCatalog);
        var key = FurClassificationNormalizer.Normalize(vehicleClass);
        return key.Length > 0 && normalizedCatalog.TryGetValue(key, out var fmt) ? fmt : FurTemplateFormat.Automotor;
    }

    /// <summary>Parsea el texto del catálogo (AUTOMOTOR/MAQUINARIA/REMOLQUES) al enum.</summary>
    public static bool TryParseFormat(string? raw, out FurTemplateFormat format)
    {
        format = FurTemplateFormat.Automotor;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        switch (raw.Trim().ToUpperInvariant())
        {
            case "AUTOMOTOR": format = FurTemplateFormat.Automotor; return true;
            case "MAQUINARIA": format = FurTemplateFormat.Maquinaria; return true;
            case "REMOLQUES": format = FurTemplateFormat.Remolques; return true;
            default: return false;
        }
    }
}
