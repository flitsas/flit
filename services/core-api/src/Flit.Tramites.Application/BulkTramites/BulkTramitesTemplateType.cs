namespace Flit.Tramites.Application.BulkTramites;

/// <summary>
/// Tipo de plantilla de carga masiva de trámites (HU #12520/#12521). Cada valor tiene su propia
/// plantilla XLSX: Matrícula y Traspaso porque son los trámites de mayor volumen, y Otros como
/// plantilla única para el resto del catálogo canónico de tipos (selector de tipo dentro del
/// archivo en vez de una plantilla por cada uno).
/// </summary>
public enum BulkTramitesTemplateType
{
    Matricula,
    Traspaso,
    Otros,
}

/// <summary>Resuelve el tipo de plantilla desde el valor de query string, o null si no es soportado.</summary>
public static class BulkTramitesTemplateTypeParser
{
    public static BulkTramitesTemplateType? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "matricula" => BulkTramitesTemplateType.Matricula,
        "traspaso" => BulkTramitesTemplateType.Traspaso,
        "otros" => BulkTramitesTemplateType.Otros,
        _ => null,
    };
}
