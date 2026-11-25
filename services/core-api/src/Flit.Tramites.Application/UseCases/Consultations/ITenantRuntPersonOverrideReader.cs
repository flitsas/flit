namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Override manual de respuesta de conductor RUNT para una persona de un tenant (HU #14642).
/// Solo se proyecta al dominio de Trámites los campos necesarios para construir
/// el <see cref="ConsultationResult"/> de cortocircuito.
/// </summary>
public sealed record TenantRuntPersonOverride(
    Guid Id,
    Guid TenantId,
    string DocumentType,
    string DocumentNumber,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string CitizenStatus,
    string DriverStatus,
    bool HasActiveLicense,
    string? LicenseCategories,
    bool HasPendingFines,
    string? NroPazYSalvo);

/// <summary>
/// Lector del catálogo de overrides manuales de RUNT por tenant (HU #14642).
/// Usado por el decorador de cortocircuito <c>TenantRuntPersonOverrideChainResolverDecorator</c>
/// en <c>Flit.Infrastructure</c> para evitar llamar al RUNT cuando existe un override activo y
/// no expirado.
/// </summary>
public interface ITenantRuntPersonOverrideReader
{
    /// <summary>
    /// Busca un override activo (is_active=true, expires_at null o futuro) para el
    /// (tenant, tipo doc, número doc). La comparación de tipo y número es case-insensitive.
    /// Devuelve <c>null</c> si no existe override o está inactivo/expirado.
    /// </summary>
    Task<TenantRuntPersonOverride?> FindActiveAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken ct);
}
