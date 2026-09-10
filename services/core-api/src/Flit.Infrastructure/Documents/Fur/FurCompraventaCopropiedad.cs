using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Narrativa y geometría de copropiedad en la compraventa autogenerada.
/// Con un solo actor por lado el contrato histórico no cambia; con 2–4 se enumeran
/// nombre + tipo + documento y las firmas se compactan en una fila.
/// </summary>
internal static class FurCompraventaCopropiedad
{
    internal static readonly (string Etiqueta, string[] Codigos)[] TiposDocumento =
    [
        ("NIT", ["NIT", "N"]),
        ("C.C.", ["CC", "C", "C.C."]),
        ("C.E.", ["CE", "E", "C.E."]),
        ("T.I", ["TI", "T", "T.I"]),
        ("P.A", ["PA", "P", "PAS", "P.A"]),
    ];

    public static List<DocumentParte> DelRol(IEnumerable<DocumentParte> partes, string rol) =>
        partes
            .Where(p => string.Equals(p.Rol, rol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Ordinal)
            .ToList();

    /// <summary>
    /// Quienes otorgan mandato / solicitan el trámite virtual: vendedores en traspaso,
    /// compradores en matrícula (misma regla que <see cref="FurDocumentData.Otorgante"/>).
    /// </summary>
    public static List<DocumentParte> Otorgantes(FurDocumentData data)
    {
        var vendedores = DelRol(data.Partes, "vendedor");
        return vendedores.Count > 0 ? vendedores : DelRol(data.Partes, "comprador");
    }

    public static bool EsMultiple(IReadOnlyCollection<DocumentParte> partes) => partes.Count > 1;

    /// <summary>Lista «NOMBRE, C.C. 123, NOMBRE2, NIT 900…» para mandato y solicitud virtual.</summary>
    public static string ListaComa(IReadOnlyList<DocumentParte> partes, string vacio = "")
    {
        if (partes.Count == 0)
            return vacio;
        return string.Join(", ", partes.Select(p =>
        {
            var nombre = string.IsNullOrWhiteSpace(p.Nombre) ? vacio : p.Nombre.Trim();
            var doc = string.IsNullOrWhiteSpace(p.Documento) ? vacio : p.Documento.Trim();
            return $"{nombre}, {EtiquetaTipo(p)} {doc}";
        }));
    }

    public static byte[]? ImagenDe(
        IReadOnlyDictionary<string, byte[]>? dict, DocumentParte? parte, string? rolFallback)
    {
        if (dict is null)
            return null;
        var key = parte is null ? rolFallback : FirmaKey(parte);
        if (!string.IsNullOrEmpty(key) && dict.TryGetValue(key, out var imagen) && imagen.Length > 0)
            return imagen;
        if (!string.IsNullOrEmpty(rolFallback)
            && !string.Equals(key, rolFallback, StringComparison.Ordinal)
            && dict.TryGetValue(rolFallback, out imagen)
            && imagen.Length > 0)
            return imagen;
        return null;
    }

    public static string? TextoDe(
        IReadOnlyDictionary<string, string>? dict, DocumentParte? parte, string? rolFallback)
    {
        if (dict is null)
            return null;
        var key = parte is null ? rolFallback : FirmaKey(parte);
        if (!string.IsNullOrEmpty(key)
            && dict.TryGetValue(key, out var texto)
            && !string.IsNullOrWhiteSpace(texto))
            return texto;
        if (!string.IsNullOrEmpty(rolFallback)
            && !string.Equals(key, rolFallback, StringComparison.Ordinal)
            && dict.TryGetValue(rolFallback, out texto)
            && !string.IsNullOrWhiteSpace(texto))
            return texto;
        return null;
    }

    public static string CondicionPropietario(IReadOnlyCollection<DocumentParte> vendedores) =>
        EsMultiple(vendedores) ? "propietarios inscritos" : "propietario(a) inscrito(a)";

    public static float EstampaAlto(int countOnSide) =>
        countOnSide >= 4 ? 18f : countOnSide == 3 ? 22f : 26f;

    public static string FirmaKey(DocumentParte parte) =>
        FurOverlayPartyKey.For(parte.Rol, parte.Ordinal);

    public static string Casillas(string? documentType)
    {
        var tipo = (documentType ?? string.Empty).Trim().ToUpperInvariant().Replace(".", "", StringComparison.Ordinal);
        return string.Join("   ", TiposDocumento.Select(t =>
        {
            var marcada = t.Codigos.Any(c =>
                string.Equals(c.Replace(".", "", StringComparison.Ordinal), tipo, StringComparison.Ordinal));
            return $"{t.Etiqueta} [{(marcada ? "X" : " ")}]";
        }));
    }

    public static string EtiquetaTipo(DocumentParte parte)
    {
        if (string.IsNullOrWhiteSpace(parte.DocumentType))
            return parte.EsJuridica ? "NIT" : "Documento";

        var tipo = parte.DocumentType.Trim();
        var norm = tipo.ToUpperInvariant().Replace(".", "", StringComparison.Ordinal);
        foreach (var t in TiposDocumento)
        {
            if (t.Codigos.Any(c =>
                    string.Equals(c.Replace(".", "", StringComparison.Ordinal), norm, StringComparison.Ordinal)))
                return t.Etiqueta;
        }

        return tipo;
    }

    /// <summary>
    /// Fragmentos del párrafo de identificación. Un único actor conserva casillas NIT/C.C./…;
    /// varios se separan por coma con tipo y número (sin repetir las cinco casillas).
    /// </summary>
    public static List<(string Text, bool Bold)> Identificacion(List<DocumentParte> partes)
    {
        var fragments = new List<(string Text, bool Bold)>();
        if (partes.Count <= 1)
        {
            var p = partes.Count == 1 ? partes[0] : null;
            fragments.Add((Val(p?.Nombre), true));
            fragments.Add((", identificado(a) con ", false));
            fragments.Add((Casillas(p?.DocumentType), false));
            fragments.Add(($" {Val(p?.Documento)}", false));
            return fragments;
        }

        for (var i = 0; i < partes.Count; i++)
        {
            if (i > 0)
                fragments.Add((", ", false));
            fragments.Add((Val(partes[i].Nombre), true));
            fragments.Add(($", {EtiquetaTipo(partes[i])} {Val(partes[i].Documento)}", false));
        }

        return fragments;
    }

    private static string Val(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
