namespace Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;

/// <summary>
/// Resultado del alta de un grant (AC2). <see cref="IsValid"/> falso indica que el
/// organismo no existe en el catálogo estático (→ 422). Con <see cref="IsValid"/>
/// verdadero, <see cref="Added"/> distingue alta nueva de idempotencia (ya existía).
/// </summary>
public sealed class AddTransitGrantResult
{
    private AddTransitGrantResult(
        bool isValid,
        bool added,
        IReadOnlyList<TransitGrantValidationError> errors)
    {
        IsValid = isValid;
        Added = added;
        Errors = errors;
    }

    public bool IsValid { get; }

    /// <summary><c>true</c> si se creó el grant; <c>false</c> si ya existía (idempotente).</summary>
    public bool Added { get; }

    public IReadOnlyList<TransitGrantValidationError> Errors { get; }

    public static AddTransitGrantResult Success(bool added) => new(true, added, []);

    public static AddTransitGrantResult Invalid(IReadOnlyList<TransitGrantValidationError> errors) =>
        new(false, false, errors);
}
