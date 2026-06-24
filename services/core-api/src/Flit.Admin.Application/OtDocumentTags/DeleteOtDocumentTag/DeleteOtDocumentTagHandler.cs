using Flit.Admin.Domain.OtDocumentTags;

namespace Flit.Admin.Application.OtDocumentTags.DeleteOtDocumentTag;

public sealed class DeleteOtDocumentTagCommand
{
    public Guid TenantId { get; init; }

    public Guid TagId { get; init; }
}

public enum DeleteOtDocumentTagStatus
{
    Deleted,
    NotFound,
}

public sealed class DeleteOtDocumentTagResult
{
    public DeleteOtDocumentTagStatus Status { get; init; }

    public static DeleteOtDocumentTagResult Deleted() =>
        new() { Status = DeleteOtDocumentTagStatus.Deleted };

    public static DeleteOtDocumentTagResult NotFound() =>
        new() { Status = DeleteOtDocumentTagStatus.NotFound };
}

/// <summary>Elimina etiqueta documental OT (soporte FE HU #10224 AC5).</summary>
public sealed class DeleteOtDocumentTagHandler
{
    private readonly IOtDocumentTagRepository _repository;

    public DeleteOtDocumentTagHandler(IOtDocumentTagRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DeleteOtDocumentTagResult> HandleAsync(
        DeleteOtDocumentTagCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var deleted = await _repository.DeleteAsync(
            command.TenantId,
            command.TagId,
            cancellationToken).ConfigureAwait(false);

        return deleted ? DeleteOtDocumentTagResult.Deleted() : DeleteOtDocumentTagResult.NotFound();
    }
}
