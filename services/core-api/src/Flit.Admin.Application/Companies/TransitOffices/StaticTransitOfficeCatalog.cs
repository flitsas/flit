using System.Text;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices;

/// <summary>
/// Catálogo estático en memoria de organismos de tránsito (HU #10192, RF13, AC1).
///
/// La HU exige un catálogo estático (sin sync externo ni consulta a
/// <c>catalogs.transit_offices</c> vía EF). Los UUIDs son fijos y deterministas para
/// que los tests y los grants puedan referenciarlos. La búsqueda es insensible a
/// mayúsculas y a tildes sobre nombre y código.
///
/// Compatible con AOT (sin reflexión): la normalización usa
/// <see cref="string.Normalize(NormalizationForm)"/> + categorías Unicode.
///
/// Uso de ejemplo:
/// <code>
/// ITransitOfficeCatalog catalog = new StaticTransitOfficeCatalog();
/// var bogota = catalog.Search("bogota"); // coincide con "…Bogotá"
/// </code>
/// </summary>
public sealed class StaticTransitOfficeCatalog : ITransitOfficeCatalog
{
    private static readonly TransitOfficeEntry[] Entries =
    [
        new(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001"), "11001", "Secretaría de Movilidad Bogotá", "11", "11001"),
        new(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002"), "05001", "Medellín — Secretaría de Movilidad", "05", "05001"),
        new(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000003"), "76001", "Cali — STTMP", "76", "76001"),
        new(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000004"), "08001", "Barranquilla — Tránsito Distrital", "08", "08001"),
        new(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000005"), "68001", "Bucaramanga — Dirección de Tránsito", "68", "68001"),
        new(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000006"), "13001", "Cartagena — DATT", "13", "13001"),
    ];

    private static readonly Dictionary<Guid, TransitOfficeEntry> ById =
        Entries.ToDictionary(e => e.Id);

    public IReadOnlyList<TransitOfficeEntry> All => Entries;

    public IReadOnlyList<TransitOfficeEntry> Search(string? term)
    {
        var normalizedTerm = Fold(term);
        if (normalizedTerm.Length == 0)
        {
            return Entries;
        }

        return [.. Entries.Where(e =>
            Fold(e.Name).Contains(normalizedTerm, StringComparison.Ordinal)
            || Fold(e.Code).Contains(normalizedTerm, StringComparison.Ordinal))];
    }

    public bool Exists(Guid id) => ById.ContainsKey(id);

    public TransitOfficeEntry? GetById(Guid id) =>
        ById.TryGetValue(id, out var entry) ? entry : null;

    /// <summary>
    /// Normaliza para comparación: recorta, pasa a minúsculas (invariante) y reemplaza
    /// las tildes/diéresis por su vocal base. No usa <see cref="string.Normalize(NormalizationForm)"/>
    /// porque el proyecto corre en modo invariante de globalización (<c>InvariantGlobalization</c>),
    /// donde la descomposición Unicode es un no-op; en su lugar se mapean explícitamente
    /// los diacríticos del español.
    /// </summary>
    private static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.Trim().ToLowerInvariant();

        var builder = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            builder.Append(ch switch
            {
                'á' or 'à' or 'ä' or 'â' or 'ã' => 'a',
                'é' or 'è' or 'ë' or 'ê' => 'e',
                'í' or 'ì' or 'ï' or 'î' => 'i',
                'ó' or 'ò' or 'ö' or 'ô' or 'õ' => 'o',
                'ú' or 'ù' or 'ü' or 'û' => 'u',
                'ñ' => 'n',
                _ => ch,
            });
        }

        return builder.ToString();
    }
}
