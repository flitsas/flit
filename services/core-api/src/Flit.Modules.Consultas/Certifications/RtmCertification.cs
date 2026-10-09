namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Una revisión técnico-mecánica certificada. Seis celdas, igual que el SOAT, más el tipo de revisión:
/// el RUNT lo manda, no va en el certificado, y se guarda porque al auditar distingue una revisión de
/// vehículo particular de una de servicio público.
/// </summary>
/// <remarks>
/// El histórico importa más aquí que en el SOAT: hay vehículos con cuatro revisiones, todas en estado
/// <c>APROBADA</c> y ninguna vigente (placa YNK04A). Quedarse con "la última que diga APROBADA" es
/// exactamente el error que produce un certificado que afirma una vigencia inexistente; por eso el
/// estado se normaliza a <see cref="VigencyStatus.Unknown"/> y la selección la hace
/// <see cref="RtmSelection"/> por fechas, no por texto.
/// </remarks>
public sealed record RtmCertification(
    CertifiedNumber CertificateNumber,
    CertifiedName Cda,
    CertifiedDate IssuedOn,
    CertifiedDate ValidFrom,
    CertifiedDate ValidUntil,
    CertifiedStatus Status,
    string? InspectionType = null)
{
    public static readonly RtmCertification Empty = new(
        CertifiedNumber.Empty, CertifiedName.Empty, CertifiedDate.Empty,
        CertifiedDate.Empty, CertifiedDate.Empty, CertifiedStatus.Empty);

    public bool HasAnyValue =>
        CertificateNumber.HasValue || Cda.HasValue || IssuedOn.HasValue
        || ValidFrom.HasValue || ValidUntil.HasValue || Status.HasValue;

    public string NaturalKey() =>
        CertificationKeys.Compose(CertificateNumber.Value, ValidUntil.Value?.ToString("yyyy-MM-dd"));

    public IReadOnlyList<string> NormalizationIssues() =>
        CertificationKeys.Unresolved(
            (nameof(CertificateNumber), CertificateNumber),
            (nameof(Cda), Cda),
            (nameof(IssuedOn), IssuedOn),
            (nameof(ValidFrom), ValidFrom),
            (nameof(ValidUntil), ValidUntil),
            (nameof(Status), Status));
}
