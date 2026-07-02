using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de prelación del expediente consolidado de traspaso (HU #10455): FUR, certificado de
/// identidad y contrato de compraventa primero, luego los documentos del checklist. A diferencia
/// de <see cref="MatriculaConsolidadoOrdering"/>, la <c>compraventa</c> SÍ se incluye (es parte del
/// expediente de traspaso); solo se excluye el propio <c>consolidado</c>.
/// </summary>
internal static class TraspasoConsolidadoOrdering
{
    private static readonly string[] Precedence =
    [
        "fur",
        "certificado_identidad",
        "compraventa",
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
