using Flit.Tramites.Application.Documents;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #14500 / ADR-0082 — bloque del párrafo 23 cuando alguna parte (o su RL) tiene tipo PPT.
/// Canónico: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c> (después de copropiedad, antes del gravamen).
/// No marca casilla tipológica en el Anexo 46; el literal se emite <b>una sola vez</b> por FUR.
/// </summary>
public static class FurPptObservation
{
    public const string Literal = "tipo de documento (PPT) Permiso por Protección Temporal";

    public static string? Compose(IReadOnlyList<DocumentParte> partes)
    {
        if (partes.Count == 0)
            return null;

        foreach (var p in partes)
        {
            if (EsPpt(p.DocumentType) || EsPpt(p.RepresentanteLegalTipoDoc))
                return Literal;
        }

        return null;
    }

    private static bool EsPpt(string? tipo) =>
        string.Equals(tipo?.Trim(), "PPT", StringComparison.OrdinalIgnoreCase);
}
