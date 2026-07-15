using Flit.Tramites.Application.UseCases.Avaluos;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Proveedor de avalúo Mercado Libre (Key <c>mercado_libre</c>, Feature #10707). Fase 1: mock,
/// lee <c>avaluo_mock_values</c> y expone la <c>mediana</c> con un número de <c>muestras</c>
/// determinista. La integración real (sites/MCO/search + mediana con outliers p10-p90) se
/// habilita por configuración sin tocar el handler (ADR-0029).
/// </summary>
internal sealed class MercadoLibreAvaluoProvider(AvaluoMockValueReader mockReader) : IAvaluoProvider
{
    private const string SourceKey = "mercado_libre";

    public string Key => SourceKey;

    public async Task<AvaluoResult> GetAvaluoAsync(AvaluoContext ctx, CancellationToken ct)
    {
        var matchKey = AvaluoMatch.KeyFor(ctx);
        if (matchKey is null)
            return AvaluoResult.NoData(SourceKey, "El vehículo no tiene VIN ni placa");

        var value = await mockReader.GetValueAsync(matchKey, SourceKey, ct);
        if (value is null)
            return AvaluoResult.NoData(SourceKey, "Sin publicaciones comparables");

        // Muestras determinista (mock): estable por VIN/placa para una demo reproducible.
        var muestras = 8 + (Math.Abs(matchKey.GetHashCode(StringComparison.Ordinal)) % 20);
        return AvaluoResult.Ok(SourceKey, value.Value, muestras);
    }
}
