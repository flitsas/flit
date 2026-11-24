namespace Flit.Admin.Application.Companies.RuntPersonOverrides;

/// <summary>
/// Vista de un override de persona RUNT para el listado del SuperAdmin (HU #14642).
/// </summary>
public sealed record TenantRuntPersonOverrideView(
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
    string? NroPazYSalvo,
    string Reason,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? UpdatedBy,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Datos de entrada para crear un override de persona RUNT (HU #14642).
/// </summary>
public sealed record CreateTenantRuntPersonOverrideRequest(
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
    string? NroPazYSalvo,
    string Reason,
    DateTimeOffset? ExpiresAt,
    Guid? RequestedBy);

/// <summary>
/// Datos de entrada para actualizar o desactivar un override existente (HU #14642).
/// </summary>
public sealed record UpdateTenantRuntPersonOverrideRequest(
    string? FirstName,
    string? SecondName,
    string? FirstLastName,
    string? SecondLastName,
    string? CitizenStatus,
    string? DriverStatus,
    bool? HasActiveLicense,
    string? LicenseCategories,
    bool? HasPendingFines,
    string? NroPazYSalvo,
    string? Reason,
    bool? IsActive,
    DateTimeOffset? ExpiresAt,
    Guid? RequestedBy);

/// <summary>
/// Error de validación de un override de persona RUNT.
/// </summary>
public sealed record TenantRuntPersonOverrideValidationError(string Field, string Message);
