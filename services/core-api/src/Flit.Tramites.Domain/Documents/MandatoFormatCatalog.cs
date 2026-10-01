namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// Un formato de contrato de mandato del catálogo del sistema (HU #13168, Feature #13118).
/// </summary>
/// <param name="Code">Código estable (<c>template_code</c>) tal como se guarda en la configuración del organismo.</param>
/// <param name="DefaultName">Nombre visible de fábrica; el Super Admin puede cambiarlo (HU #13171).</param>
/// <param name="DefaultAssignmentMode">
/// Tipo de mandato asociado de fábrica (<c>signer</c> | <c>institutional</c> | <c>open</c>). Es el valor por
/// defecto de la redacción: la regla por compañía y organismo (F5) sigue mandando sobre él.
/// </param>
/// <param name="Variante">
/// Redacción base que emite el generador; <c>null</c> para <c>auto</c>, que no es una redacción sino una
/// delegación en la plantilla de sistema del organismo.
/// </param>
public sealed record MandatoFormatDefinition(
    string Code,
    string DefaultName,
    string DefaultAssignmentMode,
    MandatoVariante? Variante)
{
    /// <summary>True si el código es una redacción real (previsualizable y editable); <c>auto</c> no lo es.</summary>
    public bool IsRedaction => Variante is not null;

    /// <summary>Código de la redacción base que emite el generador, o <c>null</c> si el formato solo delega.</summary>
    public string? BaseRedaction => IsRedaction ? Code : null;
}

/// <summary>
/// <b>Única fuente</b> de los formatos de contrato de mandato válidos (HU #13168). Antes la lista vivía
/// duplicada en <c>MandateConfigAdminService</c>, en la lista de Plataforma, en la vista previa del hub OT y en
/// el frontend; ahora todas consultan este catálogo. La lista la gestiona el equipo FLIT: el Super Admin edita
/// los formatos existentes pero no crea ni elimina (decisión del PO de la Feature #13118). Agregar un formato
/// nuevo exige añadirlo aquí y su redacción al generador.
/// </summary>
public static class MandatoFormatCatalog
{
    private static readonly MandatoFormatDefinition[] Formats =
    [
        new(MandatoTemplateResolver.Auto, "Automática (según el organismo)",
            MandatoAssignmentModeCodes.Signer, Variante: null),
        new(MandatoTemplateResolver.Generico, "Genérico",
            MandatoAssignmentModeCodes.Signer, MandatoVariante.Generico),
        new(MandatoTemplateResolver.Sabaneta, "Sabaneta",
            MandatoAssignmentModeCodes.Institutional, MandatoVariante.Sabaneta),
        new(MandatoTemplateResolver.Bello, "Bello",
            MandatoAssignmentModeCodes.Signer, MandatoVariante.Bello),
        new(MandatoTemplateResolver.Municipio, "Envigado, Funza y Medellín",
            MandatoAssignmentModeCodes.Signer, MandatoVariante.Municipio),
    ];

    /// <summary>Todos los formatos, en el orden en que se muestran (la automática primero).</summary>
    public static IReadOnlyList<MandatoFormatDefinition> All => Formats;

    /// <summary>Códigos válidos para <c>template_code</c> (incluye <c>auto</c>), ordenados como el catálogo.</summary>
    public static IReadOnlyList<string> Codes { get; } = Formats.Select(f => f.Code).ToArray();

    /// <summary>Códigos de redacciones reales (sin <c>auto</c>): los previsualizables y editables.</summary>
    public static IReadOnlyList<string> RedactionCodes { get; } =
        Formats.Where(f => f.IsRedaction).Select(f => f.Code).ToArray();

    /// <summary>Formato por código (sin distinguir mayúsculas ni espacios); <c>null</c> si no existe.</summary>
    public static MandatoFormatDefinition? Find(string? code)
    {
        var normalized = code?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        return Formats.FirstOrDefault(f => string.Equals(f.Code, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True si el código está en el catálogo (incluye <c>auto</c>).</summary>
    public static bool Contains(string? code) => Find(code) is not null;

    /// <summary>True si el código es una redacción real; <c>auto</c> y los desconocidos dan false.</summary>
    public static bool IsRedaction(string? code) => Find(code)?.IsRedaction == true;
}
