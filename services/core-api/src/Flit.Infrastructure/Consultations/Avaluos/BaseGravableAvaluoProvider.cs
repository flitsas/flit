using Flit.Tramites.Application.UseCases.Avaluos;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Proveedor de avalúo "base gravable" (Key <c>base_gravable</c>, Feature #10707). Fase 1: mock,
/// lee <c>avaluo_mock_values</c> por VIN/placa. Sirve como referencia en el desglose; se puede
/// sustituir por la integración real activándolo por configuración sin tocar el handler (ADR-0029).
/// </summary>
internal sealed class BaseGravableAvaluoProvider(AvaluoMockValueReader mockReader) : IAvaluoProvider
{
    private const string SourceKey = "base_gravable";

    public string Key => SourceKey;

    public async Task<AvaluoResult> GetAvaluoAsync(AvaluoContext ctx, CancellationToken ct)
    {
        var matchKey = AvaluoMatch.KeyFor(ctx);
        if (matchKey is null)
            return AvaluoResult.NoData(SourceKey, "El vehículo no tiene VIN ni placa");

        var value = await mockReader.GetValueAsync(matchKey, SourceKey, ct);
        return value is null
            ? AvaluoResult.NoData(SourceKey, "Sin valor de base gravable para el vehículo")
            : AvaluoResult.Ok(SourceKey, value.Value);
    }
}
