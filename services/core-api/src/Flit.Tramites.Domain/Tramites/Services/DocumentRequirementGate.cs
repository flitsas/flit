namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Un requisito documental del tipo, en su forma mínima para evaluar el gate de documentos (CFD-06).
/// </summary>
public sealed record DocumentRequirementItem(
    string DocumentTypeCode,
    bool IsRequired,
    bool IsDummy,
    /// <summary>
    /// Lo produce FLIT (consulta, FUR, consolidado). Se asocia en Documental para el expediente,
    /// pero no se pide carga ni bloquea radicación.
    /// </summary>
    bool IsSystemGenerated = false);

/// <summary>
/// Gate puro (sin IO) del paso de documentos (CFD-06): computa los códigos bloqueantes de los
/// documentos <c>is_required</c> que aún no se han cargado. Los documentos <c>is_dummy</c> nunca
/// bloquean (se muestran en el checklist pero no vetan el avance). Lo consumirá el
/// <c>DynamicGateEvaluator</c> del wizard dinámico (HU-BE-06).
/// </summary>
public static class DocumentRequirementGate
{
    /// <summary>Prefijo del código de bloqueo por documento faltante: <c>DOCUMENT_{CODE}_REQUIRED</c>.</summary>
    public static string BlockerFor(string documentTypeCode) =>
        $"DOCUMENT_{documentTypeCode.ToUpperInvariant()}_REQUIRED";

    /// <summary>
    /// Códigos bloqueantes de los requisitos obligatorios (no dummy) cuyo documento no está en
    /// <paramref name="uploadedCodes"/>. Lista vacía si nada bloquea.
    /// </summary>
    public static IReadOnlyList<string> MissingRequired(
        IEnumerable<DocumentRequirementItem> requirements,
        IReadOnlySet<string> uploadedCodes)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(uploadedCodes);

        var blockers = new List<string>();
        foreach (var req in requirements)
        {
            if (!req.IsRequired || req.IsDummy || req.IsSystemGenerated)
                continue;
            if (!uploadedCodes.Contains(req.DocumentTypeCode))
                blockers.Add(BlockerFor(req.DocumentTypeCode));
        }

        return blockers;
    }
}
