using Flit.Tramites.Application.UseCases.Avaluos;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>Resuelve la clave de match (VIN preferente; placa como fallback) desde el contexto.</summary>
internal static class AvaluoMatch
{
    public static string? KeyFor(AvaluoContext ctx)
    {
        var vin = Get(ctx, "vin");
        if (!string.IsNullOrWhiteSpace(vin))
            return vin.Trim().ToUpperInvariant();

        var plate = Get(ctx, "plate");
        return string.IsNullOrWhiteSpace(plate) ? null : plate.Trim().ToUpperInvariant();
    }

    private static string? Get(AvaluoContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var value) ? value : null;
}
