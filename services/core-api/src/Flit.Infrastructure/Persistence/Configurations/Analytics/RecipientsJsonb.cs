using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>
/// Conversión List&lt;string&gt; ⇄ jsonb para las columnas <c>recipients</c> de las tablas de
/// programación/alertas (Reportes 2.0, HU-D). El converter serializa a JSON compacto; el comparer
/// hace que EF detecte cambios por CONTENIDO (no por referencia de la lista) — sin él, EF
/// emite ValueComparer warnings y no rastrea ediciones in-place de la lista.
/// </summary>
internal static class RecipientsJsonb
{
    private static readonly JsonSerializerOptions Options = JsonSerializerOptions.Default;

    public static readonly ValueConverter<List<string>, string> Converter = new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<List<string>>(v, Options) ?? new List<string>());

    public static readonly ValueComparer<List<string>> Comparer = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, StringComparer.Ordinal.GetHashCode(s))),
        v => v.ToList());
}
