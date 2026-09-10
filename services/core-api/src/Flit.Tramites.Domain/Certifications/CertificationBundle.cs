namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Todo lo que una consulta externa certificó, en vocabulario canónico y sin rastro del proveedor que
/// lo produjo. Es la única moneda que cruza de la capa de proveedores hacia la persistencia.
/// </summary>
/// <remarks>
/// Un proveedor de vehículo llena <see cref="SoatHistory"/>, <see cref="RtmHistory"/> y
/// <see cref="Vehicle"/>; uno de registro mercantil llena <see cref="MerchantRegistrations"/>. Ninguno
/// necesita saber de la existencia del otro, y añadir un quinto proveedor no obliga a tocar a los
/// cuatro anteriores.
///
/// <para>Se guardan los <b>históricos completos</b> aunque el certificado imprima solo la vigente
/// (D9). El coste es cero —ya vino en la misma respuesta— y es lo que permite cambiar el criterio de
/// selección más adelante sin volver a consultar.</para>
/// </remarks>
public sealed record CertificationBundle(
    IReadOnlyList<SoatCertification> SoatHistory,
    IReadOnlyList<RtmCertification> RtmHistory,
    IReadOnlyList<MerchantRegistration> MerchantRegistrations,
    VehicleRegistrationFacts Vehicle)
{
    public static readonly CertificationBundle Empty =
        new([], [], [], VehicleRegistrationFacts.Empty);

    public static CertificationBundle ForVehicle(
        IReadOnlyList<SoatCertification> soat,
        IReadOnlyList<RtmCertification> rtm,
        VehicleRegistrationFacts? vehicle = null) =>
        new(soat, rtm, [], vehicle ?? VehicleRegistrationFacts.Empty);

    public static CertificationBundle ForCompany(MerchantRegistration registration) =>
        new([], [], [registration], VehicleRegistrationFacts.Empty);

    /// <summary>¿Hay algo que persistir? Un bundle vacío no genera filas ni sobrescribe nada.</summary>
    public bool HasAnyValue =>
        SoatHistory.Any(p => p.HasAnyValue)
        || RtmHistory.Any(r => r.HasAnyValue)
        || MerchantRegistrations.Any(m => m.HasAnyValue)
        || Vehicle.HasAnyValue;
}

/// <summary>
/// Respuesta cruda del proveedor, <b>ya sanitizada</b>, tal como se va a persistir.
/// </summary>
/// <remarks>
/// Guardarla es lo que hace reparable un mapeo equivocado: hoy, cuando el DTO de un proveedor omite un
/// campo, no queda ninguna evidencia de que el proveedor lo mandó — el modelo deducido de fixtures se
/// vuelve profecía autocumplida. Con el crudo, corregir el mapper y reprocesar no cuesta una nueva
/// consulta.
///
/// <para><b>PII</b>: el payload del RUES incluye nombres y documentos de representantes legales dentro
/// del texto de facultades. Va marcado <c>@pii:high</c>, se sanitiza antes de escribir y no se vuelca
/// en trazas, logs, PRs ni comentarios.</para>
/// </remarks>
public sealed record RawProviderPayload(
    string ProviderKey,
    string SubjectKind,
    string? SubjectKey,
    string PayloadJson,
    DateTimeOffset QueriedAt)
{
    public const string VehicleSubject = "vehicle";
    public const string CompanySubject = "company";
    public const string PersonSubject = "person";
}
