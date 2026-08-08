namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Registro mercantil (RUES) de una persona jurídica que participa en el trámite, identificada por NIT.
/// </summary>
/// <remarks>
/// Sustituye al snapshot congelado en la llave <c>rues_snapshots_json</c>. La diferencia no es de
/// formato: aquel vivía en <c>field_values</c>, que es inmutable fuera de borrador, y por eso una
/// compañía sin snapshot no tenía forma de conseguirlo salvo consultando <b>en vivo al generar el
/// PDF</b> — una llamada saliente, cobrada, en cada regeneración, que además deja el documento a
/// merced de que el proveedor esté arriba.
///
/// <para><see cref="LegalRepresentatives"/> es información que hoy se paga y se tira. Se persiste
/// porque el coste marginal es cero y porque el certificado la va a necesitar; <b>contiene PII</b>
/// (nombres y documentos) y la columna va marcada <c>@pii:high</c>.</para>
/// </remarks>
public sealed record MerchantRegistration(
    string Nit,
    CertifiedName BusinessName,
    CertifiedNumber RegistrationNumber,
    CertifiedStatus Status,
    CertifiedDate RegisteredOn,
    CertifiedDate RenewedOn,
    CertifiedName ChamberOfCommerce,
    CertifiedName Category,
    CertifiedName Address,
    CertifiedName City,
    IReadOnlyList<LegalRepresentative> LegalRepresentatives)
{
    public static MerchantRegistration EmptyFor(string nit) => new(
        nit, CertifiedName.Empty, CertifiedNumber.Empty, CertifiedStatus.Empty,
        CertifiedDate.Empty, CertifiedDate.Empty, CertifiedName.Empty, CertifiedName.Empty,
        CertifiedName.Empty, CertifiedName.Empty, []);

    /// <summary>
    /// ¿Hay algo que certificar? El NIT solo no cuenta: lo aporta el trámite, no el RUES. Sin al menos
    /// un dato del registro, no se emite certificado (D1/D4: no se rellena con una consulta en vivo).
    /// </summary>
    public bool HasAnyValue =>
        BusinessName.HasValue || RegistrationNumber.HasValue || Status.HasValue
        || RegisteredOn.HasValue || RenewedOn.HasValue || ChamberOfCommerce.HasValue
        || Category.HasValue || Address.HasValue || City.HasValue
        || LegalRepresentatives.Count > 0;

    /// <summary>El NIT identifica el registro dentro del trámite: una compañía, una fila.</summary>
    public string NaturalKey() => CertificationKeys.Compose(Nit);

    public IReadOnlyList<string> NormalizationIssues() =>
        CertificationKeys.Unresolved(
            (nameof(BusinessName), BusinessName),
            (nameof(RegistrationNumber), RegistrationNumber),
            (nameof(Status), Status),
            (nameof(RegisteredOn), RegisteredOn),
            (nameof(RenewedOn), RenewedOn),
            (nameof(ChamberOfCommerce), ChamberOfCommerce),
            (nameof(Category), Category),
            (nameof(Address), Address),
            (nameof(City), City));
}

/// <summary>
/// Representante legal declarado en el RUES. <b>PII</b>: nombre, tipo y número de documento.
/// </summary>
/// <remarks>
/// El proveedor real entrega esto como objeto estructurado, no como el texto plano que asume el modelo
/// actual — divergencia que el mock tapa y que revienta la deserialización contra el RUES de verdad.
/// <see cref="Powers"/> es texto libre y puede incluir nombres y documentos embebidos: no se vuelca en
/// trazas ni en comentarios.
/// </remarks>
public sealed record LegalRepresentative(
    string? Name,
    string? DocumentType,
    string? DocumentNumber,
    string? Role,
    string? Powers)
{
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(Name)
        || !string.IsNullOrWhiteSpace(DocumentNumber)
        || !string.IsNullOrWhiteSpace(Role);
}
