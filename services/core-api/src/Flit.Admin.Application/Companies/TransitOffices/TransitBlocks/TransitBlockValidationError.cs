namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks;

/// <summary>Error de validación de bloqueo de OT (HU #12407 → 422).</summary>
public sealed record TransitBlockValidationError(string Field, string Message, string? Value);
