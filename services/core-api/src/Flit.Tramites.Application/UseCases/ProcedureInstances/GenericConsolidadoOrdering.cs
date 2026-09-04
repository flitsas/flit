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
        "certificado_identidad_vendedor",
    ];

    // Ningún expediente consolidado puede ser PARTE de otro: se excluyen los dos tipos.
    // Faltaba `consolidado_maestro`, y por eso al aprobar el organismo de tránsito se duplicaba todo
    // el expediente: la aprobación genera el maestro (que ya contiene TODOS los documentos) e invalida
    // el consolidado del wizard, así que la siguiente regeneración lo mezclaba como un adjunto más y
    // cada documento salía dos veces.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "consolidado_maestro",
    };

    /// <summary>Orden genérico por defecto (documentos generados primero, resto por fecha).</summary>
    internal static IReadOnlyList<ProcedureInstanceAttachment> SelectOrdered(
        IEnumerable<ProcedureInstanceAttachment> attachments) =>
        SelectByPrecedence(attachments, DefaultPrecedence);

    /// <summary>
    /// Orden del consolidado maestro (Feature #10701 / HU #10706 AC1) a partir de la matriz
    /// documental resuelta del tenant/OT (<c>ResolvedDocumentMatrixResolver</c> /
    /// <c>ot_document_precedence</c>). Los documentos generados (FUR, licencia, certificados)
    /// conservan su prelación de cabecera; luego van los documentos en el orden de la matriz; y
    /// los no clasificados (ni generados ni en la matriz) quedan al final como "Anexos", por fecha.
    /// </summary>
    /// <param name="matrixCodes">Códigos de documento en el orden resuelto de la matriz.</param>
    internal static IReadOnlyList<ProcedureInstanceAttachment> SelectByResolvedMatrix(
        IEnumerable<ProcedureInstanceAttachment> attachments,
        IReadOnlyList<string> matrixCodes)
    {
        // Cabecera de documentos generados + orden de la matriz (sin duplicar los ya listados).
        var precedence = new List<string>(DefaultPrecedence);
        foreach (var code in matrixCodes)
        {
            if (!precedence.Contains(code, StringComparer.OrdinalIgnoreCase))
                precedence.Add(code);
        }

        return SelectByPrecedence(attachments, precedence);
    }

    /// <summary>
    /// Orden por una prelación explícita (p. ej. la matriz documental configurada, RF26).
    /// Los tipos no listados van al final, por fecha de carga. Los certificados de identidad con
    /// sufijo de ordinal (múltiple propietario) heredan el rank de su familia base.
    /// </summary>
    internal static IReadOnlyList<ProcedureInstanceAttachment> SelectByPrecedence(
        IEnumerable<ProcedureInstanceAttachment> attachments,
        IReadOnlyList<string> precedence) =>
        ConsolidadoAttachmentRank.OrderByPrecedence(
            attachments,
            precedence,
            a => !Excluded.Contains(a.Tipo)
                 && !a.Tipo.StartsWith("biometric_", StringComparison.OrdinalIgnoreCase)
                 && IsMergeableMime(a.Mimetype));


    private static bool IsMergeableMime(string? mimetype) =>
        string.Equals(mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/webp", StringComparison.OrdinalIgnoreCase);
}
