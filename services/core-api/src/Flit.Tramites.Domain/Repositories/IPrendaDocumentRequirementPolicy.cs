namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Resuelve si el documento de prenda es obligatorio para el par compañía + OT del trámite.
/// Default: obligatorio. Opt-out (check activo) ⇒ opcional. Snapshot al <c>CreatedAt</c> del trámite.
/// </summary>
public interface IPrendaDocumentRequirementPolicy
{
    /// <summary>
    /// <c>true</c> = exige documento de prenda (default). <c>false</c> = opt-out vigente o sin OT/tenant.
    /// </summary>
    Task<bool> IsRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        DateTimeOffset procedureCreatedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Default permisivo para tests (nunca exige).</summary>
public sealed class NullPrendaDocumentRequirementPolicy : IPrendaDocumentRequirementPolicy
{
    public static NullPrendaDocumentRequirementPolicy Instance { get; } = new();

    public Task<bool> IsRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        DateTimeOffset procedureCreatedAt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
