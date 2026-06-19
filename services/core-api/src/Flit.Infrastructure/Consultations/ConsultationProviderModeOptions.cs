namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Toggle real|mock por proveedor. Cargado desde variables de entorno *_MODE.
/// "real" → llama al endpoint HTTP documentado.
/// "mock" (default) → devuelve JSON canónico con la misma forma del contrato.
/// Seleccionable por proveedor: VERIFIK_VEHICLE_MODE, VERIFIK_SIMIT_MODE,
/// VERIFIK_RNMC_MODE, INTEMPO_MODE.
/// </summary>
public sealed class ConsultationProviderModeOptions
{
    public const string SectionName = "ConsultationProviderModes";

    public string VerifikVehicleMode { get; set; } = "real";
    public string VerifikSimitMode { get; set; } = "mock";
    public string VerifikRnmcMode { get; set; } = "mock";
    public string IntempoMode { get; set; } = "mock";

    public static bool IsMock(string mode) =>
        !string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase);
}
