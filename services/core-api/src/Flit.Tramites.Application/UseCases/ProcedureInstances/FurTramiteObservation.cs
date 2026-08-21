using Flit.Tramites.Application.Documents;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bloques de observaciones del párrafo 23 ligados al tipo (leasing / unilateral).
/// Fuente: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>.
/// </summary>
public static class FurTramiteObservation
{
    public static string? Compose(string? tipologiaCodigo, IReadOnlyList<DocumentParte> partes)
    {
        var code = tipologiaCodigo?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code is "MATRICULA_LEASING")
            return ComposeLeasing(partes);
        if (code is "TRASPASO_UNILATERAL")
            return ComposeUnilateral(partes);
        return null;
    }

    private static string? ComposeLeasing(IReadOnlyList<DocumentParte> partes)
    {
        var propietario = Find(partes, "comprador");
        var locatario = Find(partes, "locatario") ?? Find(partes, "comprador");
        if (propietario is null || locatario is null)
            return null;
        if (string.IsNullOrWhiteSpace(propietario.Nombre) || string.IsNullOrWhiteSpace(locatario.Nombre))
            return null;
        if (ReferenceEquals(propietario, locatario) && Find(partes, "locatario") is null)
            return null;

        var tipo = string.IsNullOrWhiteSpace(locatario.DocumentType) ? "-" : locatario.DocumentType.Trim();
        var numero = string.IsNullOrWhiteSpace(locatario.Documento) ? "-" : locatario.Documento.Trim();
        return $"Matrícula con locatario por Leasing de {propietario.Nombre.Trim()} a LOCATARIO TIPO DE DOCUMENTO {tipo}, NÚMERO DE DOCUMENTO {numero}";
    }

    private static string? ComposeUnilateral(IReadOnlyList<DocumentParte> partes)
    {
        var locatario = Find(partes, "locatario") ?? Find(partes, "comprador");
        if (locatario is null || string.IsNullOrWhiteSpace(locatario.Nombre))
            return null;

        var tipo = string.IsNullOrWhiteSpace(locatario.DocumentType) ? "-" : locatario.DocumentType.Trim();
        var numero = string.IsNullOrWhiteSpace(locatario.Documento) ? "-" : locatario.Documento.Trim();
        return $"Traspaso unilateral por leasing a {locatario.Nombre.Trim()}., tipo de documento {tipo}, número de documento {numero}.";
    }

    private static DocumentParte? Find(IReadOnlyList<DocumentParte> partes, string rol) =>
        partes.FirstOrDefault(p => string.Equals(p.Rol, rol, StringComparison.OrdinalIgnoreCase));
}
