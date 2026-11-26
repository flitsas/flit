using System.Globalization;
using System.Text;

namespace Flit.Platform.Sdk.IaConsumo;

/// <summary>
/// Semilla de <c>ia_tarifa_modelo</c> (contrato ia-consumo v1): <see cref="Anthropic"/>, de la tabla oficial de precios, en
/// USD por millón de tokens; la escritura de caché es la de 5 minutos (1,25× la entrada). <see cref="OtrosProveedores"/>
/// (OpenAI, Kimi, DeepSeek) llegó después y solo la siembra core-api (DDL 206), que es el único que los llama. Un proveedor
/// o modelo sin tarifa queda con costo NULL. Claude Haiku 5.5 cobra por tramos según el tamaño del prompt: se siembra el de ≤100K tokens,
/// igual que FLITO (un prompt mayor queda subestimado; es un costo estimado). La de Anthropic es la misma lista
/// en todas las bases de FLIT: core-api la lleva en su DDL 203 (una prueba lo compara) y los demás servicios la insertan
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

    /// <summary>Vigencia de las tarifas de OpenAI, Kimi y DeepSeek (el día en que se tomaron de sus páginas oficiales).</summary>
    public static readonly DateOnly VigenteDesdeOtros = new(2026, 11, 26);

    /// <summary>
    /// OpenAI, Kimi y DeepSeek, en USD/MTok, de sus páginas oficiales del 2026-11-26 (precio estándar, sin Batch):
    /// developers.openai.com/api/docs/pricing, platform.kimi.ai/docs/pricing/chat y api-docs.deepseek.com/quick_start/pricing.
    /// El modelo es el que DEVUELVE la API (la tarifa se busca por <c>modelo_respondido</c>): DeepSeek responde
    /// <c>deepseek-flash</c> aunque se pida <c>deepseek-chat</c>; OpenAI responde con fecha (<c>gpt-4o-2024-08-06</c>) y
    /// la calculadora la quita. DeepSeek cobra la mitad fuera de su hora pico: se siembra la tarifa pico (el costo puede
    /// quedar sobreestimado, nunca por debajo). Kimi K3: la escritura de caché es la de 5 minutos. OpenAI y DeepSeek no
    /// cobran escritura de caché.
    /// </summary>
    public static readonly IReadOnlyList<(string Proveedor, string Modelo, decimal Entrada, decimal Salida, decimal CacheLectura, decimal CacheEscritura)> OtrosProveedores =
    [
        (IaProveedores.OpenAi, "gpt-4o", 2.50m, 10.00m, 1.25m, 0m),
        (IaProveedores.OpenAi, "gpt-4o-mini", 0.15m, 0.60m, 0.075m, 0m),
        (IaProveedores.Kimi, "kimi-k3", 3.00m, 15.00m, 0.30m, 3.00m),
        (IaProveedores.DeepSeek, "deepseek-flash", 0.30m, 1.20m, 0.006m, 0m),
    ];

    /// <summary><c>INSERT … ON CONFLICT DO NOTHING</c> idempotente de la semilla en <c>&lt;schema&gt;.ia_tarifa_modelo</c>.</summary>
    public static string Sql(string schema) =>
        Insert(schema, Anthropic.Select(t => (IaProveedores.Anthropic, t.Modelo, VigenteDesde, t.Entrada, t.Salida, t.CacheLectura, t.CacheEscritura)).ToList());

    /// <summary>Lo mismo que <see cref="Sql"/> para <see cref="OtrosProveedores"/> (vigentes desde <see cref="VigenteDesdeOtros"/>).</summary>
    public static string SqlOtrosProveedores(string schema) =>
        Insert(schema, OtrosProveedores.Select(t => (t.Proveedor, t.Modelo, VigenteDesdeOtros, t.Entrada, t.Salida, t.CacheLectura, t.CacheEscritura)).ToList());

    private static string Insert(
        string schema,
        IReadOnlyList<(string Proveedor, string Modelo, DateOnly Vigente, decimal Entrada, decimal Salida, decimal CacheLectura, decimal CacheEscritura)> filas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(schema).Append(".ia_tarifa_modelo\n")
          .Append("  (proveedor, modelo, vigente_desde, precio_entrada_mtok, precio_salida_mtok, precio_cache_lectura_mtok, precio_cache_escritura_mtok)\nVALUES\n");
        for (var i = 0; i < filas.Count; i++)
        {
            var (proveedor, modelo, vigente, entrada, salida, lectura, escritura) = filas[i];
            sb.Append(CultureInfo.InvariantCulture,
                $"  ('{proveedor}', '{modelo}', DATE '{vigente:yyyy-MM-dd}', {entrada:0.00####}, {salida:0.00####}, {lectura:0.00####}, {escritura:0.00####})");
            sb.Append(i < filas.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("ON CONFLICT (proveedor, modelo, vigente_desde) DO NOTHING;");
        return sb.ToString();
    }
}
