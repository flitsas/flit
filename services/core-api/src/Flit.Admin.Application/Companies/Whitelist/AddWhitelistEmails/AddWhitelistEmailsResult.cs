namespace Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;

/// <summary>
/// Resultado del alta masiva. <see cref="IsValid"/> falso indica fallo de validación
/// (AC5 → HTTP 422) sin persistir nada; verdadero indica alta atómica exitosa
/// (AC4 → HTTP 201) con el detalle de correos insertados y omitidos (duplicados).
/// </summary>
public sealed class AddWhitelistEmailsResult
{
    private AddWhitelistEmailsResult(
        bool isValid,
        IReadOnlyList<string> addedEmails,
        IReadOnlyList<string> skippedEmails,
        IReadOnlyList<WhitelistValidationError> errors)
    {
        IsValid = isValid;
        AddedEmails = addedEmails;
        SkippedEmails = skippedEmails;
        Errors = errors;
    }

    public bool IsValid { get; }

    public IReadOnlyList<string> AddedEmails { get; }

    public IReadOnlyList<string> SkippedEmails { get; }

    public IReadOnlyList<WhitelistValidationError> Errors { get; }

    public static AddWhitelistEmailsResult Success(
        IReadOnlyList<string> addedEmails,
        IReadOnlyList<string> skippedEmails) =>
        new(true, addedEmails, skippedEmails, []);

    public static AddWhitelistEmailsResult Invalid(IReadOnlyList<WhitelistValidationError> errors) =>
        new(false, [], [], errors);
}
