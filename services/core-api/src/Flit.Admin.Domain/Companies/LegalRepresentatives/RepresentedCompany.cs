namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Agregado de compañía representada (HU #10900, ADR-0033): dimensión por NIT, aislada por tenant.
/// La comparten representantes y escrituras. Se upserta por <c>(tenant, NIT)</c>: si ya existe, se
/// actualizan sus datos mutables; si no, se crea. <see cref="DocumentNumber"/> (NIT) es PII (Ley
/// 1581): no loguear ni exponer en errores.
/// </summary>
public sealed class RepresentedCompany
{
    private RepresentedCompany(
        Guid id,
        Guid tenantId,
        string documentType,
        string documentNumber,
        string name,
        string? email,
        string? address,
        string? city,
        string? phone)
    {
        Id = id;
        TenantId = tenantId;
        DocumentType = documentType;
        DocumentNumber = documentNumber;
        Name = name;
        Email = email;
        Address = address;
        City = city;
        Phone = phone;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string DocumentType { get; }

    /// <summary>NIT de la compañía. PII (Ley 1581): no loguear ni exponer.</summary>
    public string DocumentNumber { get; }

    public string Name { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>
    /// Crea la dimensión de compañía representada. <paramref name="documentType"/> por defecto 'NIT'.
    /// </summary>
    public static RepresentedCompany Create(
        Guid tenantId,
        string documentNumber,
        string name,
        string? email = null,
        string? address = null,
        string? city = null,
        string? phone = null,
        string documentType = "NIT",
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("El tenant de la compañía representada es obligatorio.", nameof(tenantId));
        }

        return new RepresentedCompany(
            id ?? Guid.NewGuid(),
            tenantId,
            documentType.Trim(),
            documentNumber.Trim(),
            name.Trim(),
            Normalize(email),
            Normalize(address),
            Normalize(city),
            Normalize(phone));
    }

    /// <summary>Rehidrata el agregado desde persistencia (sin aplicar invariantes de alta).</summary>
    public static RepresentedCompany Rehydrate(
        Guid id,
        Guid tenantId,
        string documentType,
        string documentNumber,
        string name,
        string? email,
        string? address,
        string? city,
        string? phone) =>
        new(id, tenantId, documentType, documentNumber, name, email, address, city, phone);

    /// <summary>
    /// Actualiza los datos mutables de la compañía (parte del upsert por NIT). El NIT y el tenant son
    /// inmutables: identifican la dimensión.
    /// </summary>
    public void UpdateDetails(string name, string? email, string? address, string? city, string? phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Email = Normalize(email);
        Address = Normalize(address);
        City = Normalize(city);
        Phone = Normalize(phone);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
