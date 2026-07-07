namespace Flit.Admin.Domain.OtRequirements;

/// <summary>
/// Requisitos configurables por organismo de tránsito (<c>admin.ot_requirements</c>, HU #10545).
/// Condicionan el comportamiento del wizard por secretaría: exigir RNMC, habilitar la ruta de
/// placa preasignada y activar/desactivar la validación de identidad.
/// </summary>
public sealed class OtRequirements
{
    public Guid TransitOfficeId { get; init; }

    /// <summary>Si la secretaría exige consulta RNMC en el preflight (default seguro: <c>false</c>).</summary>
    public bool RequiresRnmc { get; init; }

    /// <summary>Si la secretaría opera con backoffice de preasignación de placa (default seguro: <c>false</c>).</summary>
    public bool AllowPlatePreassign { get; init; }

    /// <summary>Si se exige la validación biométrica de identidad (default seguro: <c>true</c>).</summary>
    public bool IdentityValidationEnabled { get; init; } = true;

    /// <summary>
    /// Requisitos por defecto seguros para un OT sin fila configurada (AC1 HU #10545):
    /// RNMC y ruta de placa deshabilitados, identidad exigida.
    /// </summary>
    public static OtRequirements SafeDefaults(Guid transitOfficeId) => new()
    {
        TransitOfficeId = transitOfficeId,
        RequiresRnmc = false,
        AllowPlatePreassign = false,
        IdentityValidationEnabled = true,
    };
}
