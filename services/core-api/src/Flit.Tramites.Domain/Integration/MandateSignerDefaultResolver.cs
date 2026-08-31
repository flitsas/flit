namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Resuelve el mandatario SUGERIDO cuando el trámite no trae elección explícita.
/// Lo usan la pantalla, la generación del PDF y el gate de aprobación.
/// </summary>
public static class MandateSignerDefaultResolver
{
    /// <summary>
    /// Compatibilidad: el <paramref name="defaultSignerId"/> es el default de <b>compañía</b>
    /// (solo si está entre candidatos). Sin default de OT.
    /// </summary>
    public static Guid? Resolve(
        IReadOnlyCollection<Guid> candidateIds,
        Guid? explicitOrSavedSignerId,
        Guid? defaultSignerId) =>
        Resolve(candidateIds, explicitOrSavedSignerId, otDefaultSignerId: null, companyDefaultSignerId: defaultSignerId);

    /// <summary>
    /// Elección del trámite → default cliente×OT (si está entre candidatos de esa gestora) →
    /// default del OT (aunque no esté en candidatos de la compañía) → vacío.
    /// Ya no se autoelige el único candidato.
    /// </summary>
    public static Guid? Resolve(
        IReadOnlyCollection<Guid> candidateIds,
        Guid? explicitOrSavedSignerId,
        Guid? otDefaultSignerId,
        Guid? companyDefaultSignerId)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);

        if (explicitOrSavedSignerId is { } chosen && chosen != Guid.Empty)
            return chosen;

        if (companyDefaultSignerId is { } company
            && company != Guid.Empty
            && candidateIds.Contains(company))
        {
            return company;
        }

        if (otDefaultSignerId is { } ot && ot != Guid.Empty)
            return ot;

        return null;
    }
}
