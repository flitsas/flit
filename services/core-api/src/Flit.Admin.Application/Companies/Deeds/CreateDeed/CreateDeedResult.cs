namespace Flit.Admin.Application.Companies.Deeds.CreateDeed;

/// <summary>
/// Resultado del alta: válido (id generado + <see cref="DeedUploadTicket"/> para que el cliente suba
/// el PDF) o inválido (422 + errores).
/// </summary>
public sealed class CreateDeedResult
{
    private CreateDeedResult(
        bool isValid,
        Guid? deedId,
        DeedUploadTicket? upload,
        IReadOnlyList<DeedValidationError> errors)
    {
        IsValid = isValid;
        DeedId = deedId;
        Upload = upload;
        Errors = errors;
    }

    public bool IsValid { get; }
    public Guid? DeedId { get; }
    public DeedUploadTicket? Upload { get; }
    public IReadOnlyList<DeedValidationError> Errors { get; }

    public static CreateDeedResult Success(Guid deedId, DeedUploadTicket upload) =>
        new(true, deedId, upload, []);

    public static CreateDeedResult Invalid(IReadOnlyList<DeedValidationError> errors) =>
        new(false, null, null, errors);
}
