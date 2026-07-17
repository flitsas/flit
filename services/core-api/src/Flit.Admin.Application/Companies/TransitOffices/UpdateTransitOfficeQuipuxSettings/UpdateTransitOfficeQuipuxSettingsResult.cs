namespace Flit.Admin.Application.Companies.TransitOffices.UpdateTransitOfficeQuipuxSettings;

/// <summary>Desenlace de la parametrización Quipux de una secretaría (HU #10710).</summary>
public enum UpdateTransitOfficeQuipuxSettingsStatus
{
    /// <summary>Parametrización persistida.</summary>
    Success,

    /// <summary>La oficina no existe o no está activa en el catálogo → 404.</summary>
    NotFound,

    /// <summary>El código DIVIPO enviado no tiene formato válido → 422.</summary>
    InvalidDivipoCode,

    /// <summary>Falta alguna de las tres banderas en el PUT → 422.</summary>
    MissingFlags,

    /// <summary>
    /// Se activó al menos una bandera sin cargar el DIVIPO → 422. Estado inconsistente: la
    /// secretaría declararía que radica sin ser elegible. El DIVIPO es obligatorio en cuanto
    /// se enciende una familia; sin banderas, un DIVIPO vacío sigue siendo válido.
    /// </summary>
    DivipoRequiredForFlags,
}

/// <summary>
/// Resultado de la parametrización Quipux. En caso de éxito devuelve el estado ya
/// normalizado que quedó persistido (DIVIPO recortado, o <c>null</c> si venía vacío), para
/// que el cliente refresque la fila sin releer el catálogo.
/// </summary>
public sealed class UpdateTransitOfficeQuipuxSettingsResult
{
    private UpdateTransitOfficeQuipuxSettingsResult(
        UpdateTransitOfficeQuipuxSettingsStatus status,
        TransitOfficeQuipuxSettingsResponse? settings)
    {
        Status = status;
        Settings = settings;
    }

    public UpdateTransitOfficeQuipuxSettingsStatus Status { get; }

    /// <summary>Estado persistido. Solo informado cuando <see cref="Status"/> es Success.</summary>
    public TransitOfficeQuipuxSettingsResponse? Settings { get; }

    public static UpdateTransitOfficeQuipuxSettingsResult Success(
        TransitOfficeQuipuxSettingsResponse settings) =>
        new(UpdateTransitOfficeQuipuxSettingsStatus.Success, settings);

    public static UpdateTransitOfficeQuipuxSettingsResult Failure(
        UpdateTransitOfficeQuipuxSettingsStatus status) =>
        new(status, null);
}

/// <summary>
/// Parametrización Quipux persistida de una secretaría (HU #10710).
/// <paramref name="Elegible"/> es derivado y se expone a propósito: es la regla de negocio
/// que el administrador necesita ver — sin DIVIPO no se radica, aunque haya banderas activas.
/// </summary>
/// <param name="TransitOfficeId">Oficina del catálogo.</param>
/// <param name="DivipoCode">Código DIVIPO, o <c>null</c> si aún no se conoce.</param>
/// <param name="QuipuxRegistration">¿Matrículas por Quipux?</param>
/// <param name="QuipuxTransfer">¿Traspasos por Quipux?</param>
/// <param name="QuipuxOther">¿Otros trámites por Quipux?</param>
/// <param name="Elegible">
/// <c>true</c> solo si hay DIVIPO y al menos una bandera activa. Con DIVIPO nulo es siempre
/// <c>false</c>: el fallo seguro frente a radicar en la secretaría equivocada.
/// </param>
public sealed record TransitOfficeQuipuxSettingsResponse(
    Guid TransitOfficeId,
    string? DivipoCode,
    bool QuipuxRegistration,
    bool QuipuxTransfer,
    bool QuipuxOther,
    bool Elegible);
