namespace Flit.Admin.Application.Companies.TransitOffices;

/// <summary>Error de validación al fijar una política de bloqueo por OT (AC3/AC4 → 422).</summary>
public sealed record OtBlockingPolicyValidationError(string Field, string Message, string? Value);
