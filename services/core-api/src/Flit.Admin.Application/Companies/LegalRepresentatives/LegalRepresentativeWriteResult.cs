namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Resultado del guardado (alta o edición) de un representante legal (HU #10901). Estados:
/// <list type="bullet">
///   <item><see cref="IsValid"/> = true → guardado; <see cref="Id"/> es el id del representante y
///     <see cref="Signals"/> puede incluir <see cref="LegalRepresentativeSignals.SinFirmaNiIdentidad"/>.</item>
///   <item><see cref="NotFound"/> = true → edición sobre un representante inexistente en el tenant (404).</item>
///   <item>caso contrario → inválido (422) con <see cref="Errors"/>.</item>
/// </list>
/// </summary>
public sealed class LegalRepresentativeWriteResult
{
    private LegalRepresentativeWriteResult(
        bool isValid,
        bool notFound,
        Guid? id,
        IReadOnlyList<string> signals,
        IReadOnlyList<LegalRepresentativeValidationError> errors)
    {
        IsValid = isValid;
        NotFound = notFound;
        Id = id;
        Signals = signals;
        Errors = errors;
    }

    public bool IsValid { get; }

    public bool NotFound { get; }

    public Guid? Id { get; }

    /// <summary>Señales no bloqueantes del guardado (p. ej. <c>sin_firma_ni_identidad</c>).</summary>
    public IReadOnlyList<string> Signals { get; }

    public IReadOnlyList<LegalRepresentativeValidationError> Errors { get; }

    public static LegalRepresentativeWriteResult Success(Guid id, IReadOnlyList<string> signals) =>
        new(true, false, id, signals, []);

    public static LegalRepresentativeWriteResult Invalid(IReadOnlyList<LegalRepresentativeValidationError> errors) =>
        new(false, false, null, [], errors);

    public static LegalRepresentativeWriteResult NotFoundResult() =>
        new(false, true, null, [], []);
}
