namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Nombre legible de un documento del expediente para el pie de página (HU #10858), derivado de su
/// tipo técnico (<c>certificado_identidad</c> → "Certificado identidad"). Cubre los tipos generados
/// más comunes con una etiqueta curada; el resto cae a una humanización genérica del tipo.
/// </summary>
public static class DocumentLabels
{
    private static readonly Dictionary<string, string> Curated = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fur"] = "Formulario Único de Registro (FUR)",
        ["compraventa"] = "Contrato de compraventa",
        ["certificado_identidad"] = "Certificado de validación de identidad",
        ["certificado_identidad_vendedor"] = "Certificado de identidad (vendedor)",
        ["certificado_rues"] = "Certificado RUES",
        ["certificado_rnmc"] = "Certificado RNMC",
        ["soat"] = "SOAT",
        ["rtm"] = "Revisión técnico-mecánica (RTM)",
        ["licencia_transito"] = "Licencia de tránsito",
        ["paz_salvo"] = "Paz y salvo",
        ["impronta"] = "Impronta",
        ["factura"] = "Factura",
        ["aduana"] = "Declaración de importación",
        ["cedulas"] = "Documentos de identidad",
    };

    public static string Display(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
            return "Documento";
        if (Curated.TryGetValue(tipo.Trim(), out var label))
            return label;

        var text = tipo.Trim().Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
