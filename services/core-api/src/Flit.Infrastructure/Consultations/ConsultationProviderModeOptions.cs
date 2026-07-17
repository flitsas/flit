namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Toggle real|mock por proveedor. Cargado desde variables de entorno *_MODE.
/// "real" → llama al endpoint HTTP documentado.
/// "mock" (default) → devuelve JSON canónico con la misma forma del contrato.
/// Seleccionable por proveedor: VERIFIK_VEHICLE_MODE, VERIFIK_SIMIT_MODE,
/// VERIFIK_RNMC_MODE, VERIFIK_CONDUCTOR_MODE, VERIFIK_RUES_MODE, INTEMPO_MODE.
/// </summary>
public sealed class ConsultationProviderModeOptions
{
    public const string SectionName = "ConsultationProviderModes";

    public string VerifikVehicleMode { get; set; } = "real";
    public string VerifikSimitMode { get; set; } = "mock";
    public string VerifikRnmcMode { get; set; } = "mock";
    public string VerifikConductorMode { get; set; } = "mock";
    public string VerifikRuesMode { get; set; } = "mock";
    public string IntempoMode { get; set; } = "mock";

    // Feature #10707 — avalúo Fasecolda. Default mock: real requiere credenciales explícitas.
    public string FasecoldaMode { get; set; } = "mock";

    // FEATURE 05 — comparendos de la fuente interna (API de registro de FLIT). Default mock:
    // el modo real pega a un API de producción.
    public string FlitFinesMode { get; set; } = "mock";

    // FEATURE 05 — comparendos de persona jurídica (KYVERUM). Default mock OBLIGATORIO hasta
    // tener credenciales verificadas: en real sin credencial válida, el 401 se traduce a un check
    // "error" = bloqueo duro no subsanable para todo traspaso con comprador persona jurídica.
    public string KyverumFinesMode { get; set; } = "mock";

    public static bool IsMock(string mode) =>
        !string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase);
}
