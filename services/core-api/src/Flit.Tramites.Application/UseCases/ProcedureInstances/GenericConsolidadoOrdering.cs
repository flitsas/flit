using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de prelación del expediente consolidado para modalidades distintas de matrícula
/// inicial y traspaso (HU #10522, RF27/41), y para el orden derivado de la matriz configurada
/// (RF26). Prioriza los documentos generados (FUR, licencia, certificado de identidad) y luego
/// el resto por fecha de carga. Excluye solo el propio consolidado.
/// </summary>
internal static class GenericConsolidadoOrdering
{
    private static readonly string[] DefaultPrecedence =
    [
        "fur",
        "licencia_transito",
        "certificado_identidad",
    ];

    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
    };

    /// <summary>Orden genérico por defecto (documentos generados primero, resto por fecha).</summary>
    internal static IReadOnlyList<ProcedureInstanceAttachment> SelectOrdered(
        IEnumerable<ProcedureInstanceAttachment> attachments) =>
        SelectByPrecedence(attachments, DefaultPrecedence);

    /// <summary>
    /// Orden por una prelación explícita (p. ej. la matriz documental configurada, RF26).
    /// Los tipos no listados van al final, por fecha de carga.
    /// </summary>
    internal static IReadOnlyList<ProcedureInstanceAttachment> SelectByPrecedence(
        IEnumerable<ProcedureInstanceAttachment> attachments,
        IReadOnlyList<string> precedence)
    {
        var rank = precedence
            .Select((tipo, index) => (tipo, index))
            .ToDictionary(x => x.tipo, x => x.index, StringComparer.OrdinalIgnoreCase);

        return attachments
            .Where(a => !Excluded.Contains(a.Tipo))
            .Where(a => !a.Tipo.StartsWith("biometric_", StringComparison.OrdinalIgnoreCase))
            .Where(a => IsMergeableMime(a.Mimetype))
            .OrderBy(a => rank.TryGetValue(a.Tipo, out var r) ? r : precedence.Count + 1)
            .ThenBy(a => a.UploadedAt)
            .ToList();
    }

    private static bool IsMergeableMime(string? mimetype) =>
        string.Equals(mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/webp", StringComparison.OrdinalIgnoreCase);
}
