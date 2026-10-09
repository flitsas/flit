namespace Flit.Tramites.Domain.Certifications;

/// <summary>Utilidades compartidas por los agregados de certificación. Sin estado, sin dependencias.</summary>
internal static class CertificationKeys
{
    /// <summary>
    /// Marcador del tramo vacío en una llave natural. Es explícito y no cadena vacía para que dos
    /// ausencias en posiciones distintas no produzcan la misma llave (<c>"A|"</c> vs <c>"|A"</c>).
    /// </summary>
    private const string Missing = "~";

    public static string Compose(params string?[] parts) =>
        string.Join('|', parts.Select(p => string.IsNullOrWhiteSpace(p) ? Missing : p!.Trim()));

    /// <summary>
    /// Campos que trajeron crudo del proveedor pero no produjeron canónico. Es la lista de trabajo
    /// para corregir un mapper sin volver a pagar la consulta.
    /// </summary>
    public static IReadOnlyList<string> Unresolved(params (string Field, ICertifiedValue Value)[] values)
    {
        List<string>? issues = null;
        foreach (var (field, value) in values)
        {
            if (!value.Unresolved)
                continue;

            issues ??= [];
            issues.Add(field);
        }

        return issues ?? (IReadOnlyList<string>)[];
    }
}
