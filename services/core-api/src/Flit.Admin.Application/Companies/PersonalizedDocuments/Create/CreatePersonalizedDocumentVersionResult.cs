namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Create;

/// <summary>Desenlace del alta de una versión.</summary>
public enum CreatePersonalizedDocumentVersionOutcome
{
    Created,
    InvalidDocumentType,
    ValidationFailed,
    ChannelNotEnabled,
}

/// <summary>
/// Resultado del alta: <see cref="CreatePersonalizedDocumentVersionOutcome.Created"/> con id + versión
/// + ticket de subida; <c>InvalidDocumentType</c> → 400 (AC1); <c>ValidationFailed</c> → 422;
/// <c>ChannelNotEnabled</c> → 409 <c>canal_no_habilitado</c> (AC4).
/// </summary>
public sealed class CreatePersonalizedDocumentVersionResult
{
    private CreatePersonalizedDocumentVersionResult(
        CreatePersonalizedDocumentVersionOutcome outcome,
        Guid? id,
        int? version,
        PersonalizedDocumentUploadTicket? upload,
        IReadOnlyList<PersonalizedDocumentValidationError> errors)
    {
        Outcome = outcome;
        Id = id;
        Version = version;
        Upload = upload;
        Errors = errors;
    }

    public CreatePersonalizedDocumentVersionOutcome Outcome { get; }
    public Guid? Id { get; }
    public int? Version { get; }
    public PersonalizedDocumentUploadTicket? Upload { get; }
    public IReadOnlyList<PersonalizedDocumentValidationError> Errors { get; }

    public static CreatePersonalizedDocumentVersionResult Created(Guid id, int version, PersonalizedDocumentUploadTicket upload) =>
        new(CreatePersonalizedDocumentVersionOutcome.Created, id, version, upload, []);

    public static CreatePersonalizedDocumentVersionResult InvalidType(PersonalizedDocumentValidationError error) =>
        new(CreatePersonalizedDocumentVersionOutcome.InvalidDocumentType, null, null, null, [error]);

    public static CreatePersonalizedDocumentVersionResult Invalid(IReadOnlyList<PersonalizedDocumentValidationError> errors) =>
        new(CreatePersonalizedDocumentVersionOutcome.ValidationFailed, null, null, null, errors);

    public static CreatePersonalizedDocumentVersionResult ChannelNotEnabled() =>
        new(CreatePersonalizedDocumentVersionOutcome.ChannelNotEnabled, null, null, null, []);
}
