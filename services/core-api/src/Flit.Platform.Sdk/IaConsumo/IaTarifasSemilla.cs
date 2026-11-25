using System.Globalization;
using System.Text;

namespace Flit.Platform.Sdk.IaConsumo;

/// <summary>
/// Semilla de <c>ia_tarifa_modelo</c> (contrato ia-consumo v1): SOLO Anthropic, de la tabla oficial de precios, en USD por
/// millón de tokens; la escritura de caché es la de 5 minutos (1,25× la entrada). Los proveedores sin precio confirmado
/// no se siembran (costo NULL). Claude Haiku 5.5 cobra por tramos según el tamaño del prompt: se siembra el de ≤100K tokens,
/// igual que FLITO (un prompt mayor queda subestimado; es un costo estimado). Es la misma lista
/// en todas las bases de FLIT: core-api la lleva en su DDL 202 (una prueba lo compara) y los demás servicios la insertan
/// con <see cref="Sql"/> en su migración.
/// </summary>
public static class IaTarifasSemilla
{
    /// <summary>Fecha de vigencia de la semilla (la del contrato v1).</summary>
    public static readonly DateOnly VigenteDesde = new(2026, 11, 18);

    /// <summary>(modelo, entrada, salida, lectura de caché, escritura de caché) en USD/MTok.</summary>
    public static readonly IReadOnlyList<(string Modelo, decimal Entrada, decimal Salida, decimal CacheLectura, decimal CacheEscritura)> Anthropic =
    [
        ("claude-haiku-4-5", 1.00m, 5.00m, 0.10m, 1.25m),
        ("claude-sonnet-4-6", 3.00m, 15.00m, 0.30m, 3.75m),
        ("claude-haiku-5-5", 0.10m, 0.50m, 0.01m, 0.125m),
        ("claude-sonnet-5", 2.00m, 10.00m, 0.20m, 2.50m),
        ("claude-sonnet-5-5", 2.00m, 10.00m, 0.10m, 2.50m),
        ("claude-opus-4-6", 5.00m, 25.00m, 0.50m, 6.25m),
        ("claude-opus-4-7", 5.00m, 25.00m, 0.50m, 6.25m),
        ("claude-opus-4-8", 5.00m, 25.00m, 0.50m, 6.25m),
        ("claude-opus-5", 5.00m, 25.00m, 0.50m, 6.25m),
        ("claude-opus-5-5", 4.00m, 20.00m, 0.20m, 5.00m),
        ("claude-fable-5-2", 8.00m, 40.00m, 0.20m, 10.00m),
    ];

    /// <summary><c>INSERT … ON CONFLICT DO NOTHING</c> idempotente de la semilla en <c>&lt;schema&gt;.ia_tarifa_modelo</c>.</summary>
    public static string Sql(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(schema).Append(".ia_tarifa_modelo\n")
          .Append("  (proveedor, modelo, vigente_desde, precio_entrada_mtok, precio_salida_mtok, precio_cache_lectura_mtok, precio_cache_escritura_mtok)\nVALUES\n");
        for (var i = 0; i < Anthropic.Count; i++)
        {
            var (modelo, entrada, salida, lectura, escritura) = Anthropic[i];
            sb.Append(CultureInfo.InvariantCulture,
                $"  ('{IaProveedores.Anthropic}', '{modelo}', DATE '{VigenteDesde:yyyy-MM-dd}', {entrada:0.00####}, {salida:0.00####}, {lectura:0.00####}, {escritura:0.00####})");
            sb.Append(i < Anthropic.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("ON CONFLICT (proveedor, modelo, vigente_desde) DO NOTHING;");
        return sb.ToString();
    }
}
