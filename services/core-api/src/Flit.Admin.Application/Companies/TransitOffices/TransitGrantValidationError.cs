namespace Flit.Admin.Application.Companies.TransitOffices;

/// <summary>Error de validación de un grant de organismo de tránsito (AC2 → 422).</summary>
public sealed record TransitGrantValidationError(string Field, string Message, string? Value);
