namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Agregado de representante legal por compañía (HU #10900, ADR-0033). Registro = par
/// <c>(compañía representada + representante)</c>, aislado por tenant. Vincula, resuelto al guardar,
/// la firma del baúl (<see cref="SignatureVaultId"/>) O la validación de identidad vigente
/// (<see cref="IdentityValidationRef"/>) — precedencia baúl &gt; identidad (coherente con
/// <c>IdentityApprovalResolver</c>). <see cref="DocumentNumber"/> es PII (Ley 1581): no loguear.
/// </summary>
public sealed class LegalRepresentative
{
    private LegalRepresentative(
        Guid id,
        Guid tenantId,
        Guid? representedCompanyId,
        string documentType,
        string documentNumber,
        string firstLastName,
        string? secondLastName,
        string name,
        string? email,
        string? address,
        string? city,
        string? phone,
        Guid? signatureVaultId,
        Guid? identityValidationRef,
        bool isActive)
    {
        Id = id;
        TenantId = tenantId;
        RepresentedCompanyId = representedCompanyId;
        DocumentType = documentType;
        DocumentNumber = documentNumber;
        FirstLastName = firstLastName;
        SecondLastName = secondLastName;
        Name = name;
        Email = email;
        Address = address;
        City = city;
        Phone = phone;
        SignatureVaultId = signatureVaultId;
        IdentityValidationRef = identityValidationRef;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    /// <summary>Compañía primaria denormalizada (nullable / deprecada desde HU #10932). Null = persona sin NITs aún.</summary>
    public Guid? RepresentedCompanyId { get; }

    public string DocumentType { get; }

    /// <summary>Documento del representante. PII (Ley 1581): no loguear ni exponer.</summary>
    public string DocumentNumber { get; }

    public string FirstLastName { get; private set; }

    public string? SecondLastName { get; private set; }

    public string Name { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Firma del baúl vinculada (vigente al guardar). Excluyente con la identidad.</summary>
    public Guid? SignatureVaultId { get; private set; }

    /// <summary>Validación de identidad vigente vinculada. Excluyente con la firma del baúl.</summary>
    public Guid? IdentityValidationRef { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>¿Tiene firma del baúl o validación de identidad resuelta?</summary>
    public bool HasSignatureOrIdentity => SignatureVaultId is not null || IdentityValidationRef is not null;

    /// <summary>Crea un representante activo, sin firma/identidad resuelta (se vincula aparte). Compañía opcional.</summary>
    public static LegalRepresentative Create(
        Guid tenantId,
        Guid? representedCompanyId,
        string documentType,
        string documentNumber,
        string firstLastName,
        string name,
        string? secondLastName = null,
        string? email = null,
        string? address = null,
        string? city = null,
        string? phone = null,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstLastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("El tenant del representante es obligatorio.", nameof(tenantId));
        }

        if (representedCompanyId == Guid.Empty)
        {
            representedCompanyId = null;
        }

        return new LegalRepresentative(
            id ?? Guid.NewGuid(),
            tenantId,
            representedCompanyId,
            documentType.Trim(),
            documentNumber.Trim(),
            firstLastName.Trim(),
            Normalize(secondLastName),
            name.Trim(),
            Normalize(email),
            Normalize(address),
            Normalize(city),
            Normalize(phone),
            signatureVaultId: null,
            identityValidationRef: null,
            isActive: true);
    }

    /// <summary>Rehidrata el agregado desde persistencia (sin aplicar invariantes de alta).</summary>
    public static LegalRepresentative Rehydrate(
        Guid id,
        Guid tenantId,
        Guid? representedCompanyId,
        string documentType,
        string documentNumber,
        string firstLastName,
        string? secondLastName,
        string name,
        string? email,
        string? address,
        string? city,
        string? phone,
        Guid? signatureVaultId,
        Guid? identityValidationRef,
        bool isActive) =>
        new(id, tenantId, representedCompanyId, documentType, documentNumber, firstLastName,
            secondLastName, name, email, address, city, phone, signatureVaultId, identityValidationRef,
            isActive);

    /// <summary>Actualiza los datos mutables de la persona (edición del registro).</summary>
    public void UpdateDetails(
        string firstLastName,
        string name,
        string? secondLastName,
        string? email,
        string? address,
        string? city,
        string? phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstLastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        FirstLastName = firstLastName.Trim();
        Name = name.Trim();
        SecondLastName = Normalize(secondLastName);
        Email = Normalize(email);
        Address = Normalize(address);
        City = Normalize(city);
        Phone = Normalize(phone);
    }

    /// <summary>
    /// Vincula la firma del baúl resuelta al guardar. Excluyente: limpia la referencia de identidad
    /// (precedencia baúl &gt; identidad).
    /// </summary>
    public void LinkSignature(Guid signatureVaultId)
    {
        if (signatureVaultId == Guid.Empty)
        {
            throw new ArgumentException("La firma del baúl a vincular es obligatoria.", nameof(signatureVaultId));
        }

        SignatureVaultId = signatureVaultId;
        IdentityValidationRef = null;
    }

    /// <summary>
    /// Vincula la validación de identidad vigente resuelta al guardar. Excluyente: limpia la firma
    /// del baúl.
    /// </summary>
    public void LinkIdentity(Guid identityValidationRef)
    {
        if (identityValidationRef == Guid.Empty)
        {
            throw new ArgumentException("La validación de identidad a vincular es obligatoria.", nameof(identityValidationRef));
        }

        IdentityValidationRef = identityValidationRef;
        SignatureVaultId = null;
    }

    /// <summary>Deja el representante sin firma ni identidad (ninguna vigente al guardar).</summary>
    public void ClearSignatureAndIdentity()
    {
        SignatureVaultId = null;
        IdentityValidationRef = null;
    }

    /// <summary>Baja lógica del representante (idempotente).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reactiva un representante dado de baja (idempotente).</summary>
    public void Reactivate() => IsActive = true;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
