namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Una póliza de SOAT tal como la certifica una fuente externa. Las seis propiedades certificadas son
/// exactamente las seis celdas de la tabla de SOAT del expediente; no hay ninguna que se derive al
/// pintar.
/// </summary>
/// <remarks>
/// El RUNT devuelve el <b>histórico completo</b> de pólizas del vehículo, no solo la última. Se
/// persisten todas (cuesta lo mismo y ya está pagado) y <see cref="SoatSelection"/> decide cuál va al
/// certificado. Guardar el histórico es lo que permite cambiar ese criterio más adelante sin volver a
/// consultar — que es justo lo que hoy no se puede hacer.
/// </remarks>
public sealed record SoatCertification(
    CertifiedNumber PolicyNumber,
    CertifiedName Insurer,
    CertifiedDate IssuedOn,
    CertifiedDate ValidFrom,
    CertifiedDate ValidUntil,
    CertifiedStatus Status)
{
    public static readonly SoatCertification Empty = new(
        CertifiedNumber.Empty, CertifiedName.Empty, CertifiedDate.Empty,
        CertifiedDate.Empty, CertifiedDate.Empty, CertifiedStatus.Empty);

    /// <summary>¿Aporta al menos un dato imprimible? Una fila sin nada no se persiste ni se certifica.</summary>
    public bool HasAnyValue =>
        PolicyNumber.HasValue || Insurer.HasValue || IssuedOn.HasValue
        || ValidFrom.HasValue || ValidUntil.HasValue || Status.HasValue;

    /// <summary>
    /// Llave natural de la póliza dentro de un trámite. Es lo que hace que reconsultar
    /// <b>actualice</b> la fila en vez de duplicarla, y que dos pólizas distintas del histórico
    /// convivan. Número + vencimiento porque una renovación conserva a veces el número.
    /// </summary>
    public string NaturalKey() =>
        CertificationKeys.Compose(PolicyNumber.Value, ValidUntil.Value?.ToString("yyyy-MM-dd"));

    /// <summary>Campos que llegaron del proveedor y no se supieron leer. Alimenta <c>normalization_issues</c>.</summary>
    public IReadOnlyList<string> NormalizationIssues() =>
        CertificationKeys.Unresolved(
            (nameof(PolicyNumber), PolicyNumber),
            (nameof(Insurer), Insurer),
            (nameof(IssuedOn), IssuedOn),
            (nameof(ValidFrom), ValidFrom),
            (nameof(ValidUntil), ValidUntil),
            (nameof(Status), Status));
}
