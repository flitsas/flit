using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de prelación del expediente consolidado de matrícula inicial: FUR y certificados
/// generados primero, luego documentos del checklist en el orden del catálogo.
/// </summary>
internal static class MatriculaConsolidadoOrdering
{
    private static readonly string[] Precedence =
    [
        "fur",
        // Licencia de Tránsito emitida por el OT al decidir el trámite: entra al consolidado
        // justo después del FUR (generar o re-generar desde cualquier módulo la incluye).
        "licencia_transito",
        "certificado_identidad",
        // HU #10589 — certificado RUES de la persona jurídica, tras el de identidad.
        "certificado_rues",
        // HU #10762 — certificado RNMC (medidas correctivas), junto a los demás certificados generados.
        "certificado_rnmc",
        "factura",
        "aduana",
        "impronta",
        "soat",
        "certificado_ambiental",
        "declaracion_aduana",
        "acta_remate",
        "oficio_judicial",
        "otro",
    ];

    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "compraventa",
    };

    internal static IReadOnlyList<ProcedureInstanceAttachment> SelectOrdered(
        IEnumerable<ProcedureInstanceAttachment> attachments)
    {
        var rank = Precedence
            .Select((tipo, index) => (tipo, index))
            .ToDictionary(x => x.tipo, x => x.index, StringComparer.OrdinalIgnoreCase);

        return attachments
            .Where(a => !Excluded.Contains(a.Tipo))
            .Where(a => !a.Tipo.StartsWith("biometric_", StringComparison.OrdinalIgnoreCase))
            .Where(a => IsMergeableMime(a.Mimetype))
            .OrderBy(a => rank.TryGetValue(a.Tipo, out var r) ? r : Precedence.Length + 1)
            .ThenBy(a => a.UploadedAt)
            .ToList();
    }

    private static bool IsMergeableMime(string? mimetype) =>
        string.Equals(mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/webp", StringComparison.OrdinalIgnoreCase);
}
