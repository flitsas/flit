namespace Flit.Admin.Application.Companies.TransitOffices;

/// <summary>Error de validación al fijar una restricción de consulta por OT (AC3/AC4 → 422).</summary>
public sealed record OtConsultationRestrictionValidationError(string Field, string Message, string? Value);
