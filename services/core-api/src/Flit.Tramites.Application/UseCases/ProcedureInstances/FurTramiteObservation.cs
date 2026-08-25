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
        if (code is "CAMBIO_LOCATARIO")
            return ComposeCambioLocatario(partes);
        return null;
    }

    /// <summary>
    /// Casilla 18 (Otros): quién deja de ser arrendatario del vehículo y quién pasa a serlo.
    /// <code>CAMBIO DE LOCATARIO por Leasing de {PROPIETARIO} a {LOCATARIO}, TIPO DE DOCUMENTO {TIPO},
    /// NÚMERO DE DOCUMENTO {NUMERO}.</code>
    /// </summary>
    /// <remarks>
    /// «Leasing de» es texto fijo de la plantilla, no parte de la razón social: es el mismo conector
    /// que ya usa <see cref="ComposeLeasing"/>, de modo que los dos trámites de leasing se leen igual
    /// en el formulario. El propietario va solo con su nombre; el tipo y el número de documento
    /// acompañan únicamente al locatario, que es la parte que entra.
    /// </remarks>
    private static string? ComposeCambioLocatario(IReadOnlyList<DocumentParte> partes)
    {
        var propietario = Find(partes, "comprador");
        var locatario = Find(partes, "locatario");

        // Sin las DOS partes no se compone: aquí no cabe el fallback al comprador que sí usan leasing y
        // unilateral, porque el trámite es precisamente el cambio de una por otra y con una sola parte
        // la frase diría que alguien se sustituye a sí mismo. Regla del artefacto: faltan datos ⇒ sí
        // casilla, sí tipo, NO se inventa el texto.
        if (propietario is null || locatario is null)
            return null;
        if (string.IsNullOrWhiteSpace(propietario.Nombre) || string.IsNullOrWhiteSpace(locatario.Nombre))
            return null;

        var tipo = string.IsNullOrWhiteSpace(locatario.DocumentType) ? "-" : locatario.DocumentType.Trim();
        var numero = string.IsNullOrWhiteSpace(locatario.Documento) ? "-" : locatario.Documento.Trim();
        return $"CAMBIO DE LOCATARIO por Leasing de {propietario.Nombre.Trim()} a {locatario.Nombre.Trim()}, "
             + $"TIPO DE DOCUMENTO {tipo}, NÚMERO DE DOCUMENTO {numero}.";
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
