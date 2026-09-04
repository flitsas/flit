using Flit.Tramites.Application.Documents;

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
        // HU #11032 — las escrituras caían a la humanización genérica ("Escritura", "Escritura
        // comprador") y el pie del consolidado no dejaba claro de qué parte era cada una.
        ["escritura"] = "Escrituras del vendedor",
        ["escritura_comprador"] = "Escrituras del comprador",
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
        // Sin curar caería a «Certificado blindaje», que es lo que ya se leía en el pie del
        // consolidado cuando el certificado viajaba como «Otro documento».
        ["certificado_blindaje"] = "Certificado de blindaje",
    };

    public static string Display(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
            return "Documento";

        var key = tipo.Trim();
        if (Curated.TryGetValue(key, out var label))
            return label;

        // Múltiple propietario: certificado_identidad_2 / certificado_identidad_vendedor_3.
        if (IdentityCertificateAttachmentTipo.IsIdentityCertificate(key))
        {
            var family = IdentityCertificateAttachmentTipo.RankKey(key);
            var ordinal = IdentityCertificateAttachmentTipo.OrdinalFromTipo(key);
            var baseLabel = Curated.TryGetValue(family, out var curated)
                ? curated
                : "Certificado de validación de identidad";
            return ordinal <= 1 ? baseLabel : $"{baseLabel} ({ordinal})";
        }

        var text = key.Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    /// Perfil de estampado (ADR-0049) del pie de página según el tipo técnico del documento. Solo el
    /// FUR (<c>tipo == "fur"</c>) usa el margen reducido <see cref="StampProfile.Formulario"/> — el
    /// perfil aplica a TODAS sus páginas (hoja 1 y hoja 2, en los tres formatos AUTOMOTOR/MAQUINARIA/
    /// REMOLQUES), no hay resolución por número de página. El resto de tipos usa
    /// <see cref="StampProfile.Default"/>.
    /// </summary>
    public static StampProfile ProfileFor(string? tipo) =>
        string.Equals(tipo?.Trim(), "fur", StringComparison.OrdinalIgnoreCase)
            ? StampProfile.Formulario
            : StampProfile.Default;
}
