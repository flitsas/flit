using Flit.Admin.Domain.OtDocumentTags;

namespace Flit.Admin.Application.OtDocumentTags.CreateOtDocumentTag;

public sealed class CreateOtDocumentTagCommand
{
    public Guid TenantId { get; init; }

    public Guid? CreatedBy { get; init; }

    public CreateOtDocumentTagRequest Request { get; init; } = new();
}

public enum CreateOtDocumentTagStatus
{
    Created,
    ValidationFailed,
    DuplicateCode,
}

public sealed record FieldError(string Field, string Message);

public sealed class CreateOtDocumentTagResult
{
    public CreateOtDocumentTagStatus Status { get; init; }

    public OtDocumentTagResponse? Tag { get; init; }

    public IReadOnlyList<FieldError> Errors { get; init; } = Array.Empty<FieldError>();

    public static CreateOtDocumentTagResult Created(OtDocumentTagResponse tag) =>
        new() { Status = CreateOtDocumentTagStatus.Created, Tag = tag };

    public static CreateOtDocumentTagResult ValidationFailed(params FieldError[] errors) =>
        new() { Status = CreateOtDocumentTagStatus.ValidationFailed, Errors = errors };

    public static CreateOtDocumentTagResult DuplicateCode() =>
        new() { Status = CreateOtDocumentTagStatus.DuplicateCode };
}

/// <summary>Crea etiqueta documental OT (HU #10222 AC3–AC4).</summary>
public sealed class CreateOtDocumentTagHandler
{
    private readonly IOtDocumentTagRepository _repository;

    public CreateOtDocumentTagHandler(IOtDocumentTagRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateOtDocumentTagResult> HandleAsync(
        CreateOtDocumentTagCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var errors = Validate(command.Request);
        if (errors.Count > 0)
        {
            return CreateOtDocumentTagResult.ValidationFailed(errors.ToArray());
        }

        var code = command.Request.Code.Trim().ToUpperInvariant();
        if (await _repository.ExistsCodeAsync(command.TenantId, code, cancellationToken).ConfigureAwait(false))
        {
            return CreateOtDocumentTagResult.DuplicateCode();
        }

        var created = await _repository.CreateAsync(
            command.TenantId,
            code,
            command.Request.Name.Trim(),
            command.Request.Color,
            command.CreatedBy,
            cancellationToken).ConfigureAwait(false);

        return CreateOtDocumentTagResult.Created(OtDocumentTagMapper.ToResponse(created));
    }

    private static List<FieldError> Validate(CreateOtDocumentTagRequest request)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors.Add(new FieldError("code", "CODE_REQUIRED"));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", "NAME_REQUIRED"));
        }

        if (!OtDocumentTagColorValidator.IsValid(request.Color))
        {
            errors.Add(new FieldError("color", "INVALID_HEX_COLOR"));
        }

        return errors;
    }
}
