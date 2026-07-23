namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Agregado de escritura (HU #10900, ADR-0033): documento PDF con descripción, vigencia
/// [<see cref="VigenciaDesde"/>, <see cref="VigenciaHasta"/>] y baja lógica
/// (<see cref="IsActive"/>), aislado por tenant. El PDF vive en storage
/// (<see cref="StoragePath"/>/<see cref="StorageSha256"/>), nunca en el agregado. La vigencia se
/// valida en el alta y se enforce en el consumo con <see cref="EstaVigente"/>.
/// </summary>
public sealed class Deed
{
    private Deed(
        Guid id,
        Guid tenantId,
        string description,
        string storagePath,
        string storageSha256,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        bool isActive)
    {
        Id = id;
        TenantId = tenantId;
        Description = description;
        StoragePath = storagePath;
        StorageSha256 = storageSha256;
        VigenciaDesde = vigenciaDesde;
        VigenciaHasta = vigenciaHasta;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string Description { get; private set; }

    public string StoragePath { get; private set; }

    public string StorageSha256 { get; private set; }

    public DateOnly VigenciaDesde { get; private set; }

    public DateOnly VigenciaHasta { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Crea una escritura activa. Valida la vigencia (<c>hasta ≥ desde</c>).</summary>
    public static Deed Create(
        Guid tenantId,
        string description,
        string storagePath,
        string storageSha256,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageSha256);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("El tenant de la escritura es obligatorio.", nameof(tenantId));
        }

        if (vigenciaHasta < vigenciaDesde)
        {
            throw new ArgumentException(
                "La vigencia_hasta no puede ser anterior a vigencia_desde.", nameof(vigenciaHasta));
        }

        return new Deed(
            id ?? Guid.NewGuid(),
            tenantId,
            description.Trim(),
            storagePath.Trim(),
            storageSha256.Trim(),
            vigenciaDesde,
            vigenciaHasta,
            isActive: true);
    }

    /// <summary>Rehidrata el agregado desde persistencia (sin aplicar invariantes de alta).</summary>
    public static Deed Rehydrate(
        Guid id,
        Guid tenantId,
        string description,
        string storagePath,
        string storageSha256,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        bool isActive) =>
        new(id, tenantId, description, storagePath, storageSha256, vigenciaDesde, vigenciaHasta, isActive);

    /// <summary>Actualiza descripción y vigencia (edición). Valida el rango de vigencia.</summary>
    public void UpdateDetails(string description, DateOnly vigenciaDesde, DateOnly vigenciaHasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (vigenciaHasta < vigenciaDesde)
        {
            throw new ArgumentException(
                "La vigencia_hasta no puede ser anterior a vigencia_desde.", nameof(vigenciaHasta));
        }

        Description = description.Trim();
        VigenciaDesde = vigenciaDesde;
        VigenciaHasta = vigenciaHasta;
    }

    /// <summary>Reemplaza el artefacto PDF (nuevo path + hash de integridad).</summary>
    public void ReplaceArtifact(string storagePath, string storageSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageSha256);
        StoragePath = storagePath.Trim();
        StorageSha256 = storageSha256.Trim();
    }

    /// <summary>Baja lógica de la escritura (idempotente).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// La escritura es consumible en <paramref name="today"/>: está activa y la fecha cae dentro de
    /// [<see cref="VigenciaDesde"/>, <see cref="VigenciaHasta"/>] (ambos inclusive).
    /// </summary>
    public bool EstaVigente(DateOnly today) =>
        IsActive && today >= VigenciaDesde && today <= VigenciaHasta;
}
