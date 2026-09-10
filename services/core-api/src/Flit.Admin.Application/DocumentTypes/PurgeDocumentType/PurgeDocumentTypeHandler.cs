using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.PurgeDocumentType;

public sealed class PurgeDocumentTypeCommand
{
    public required Guid Id { get; init; }
}

public enum PurgeDocumentTypeOutcome
{
    Purged,
    NotFound,
}

public sealed class PurgeDocumentTypeResult
{
    private PurgeDocumentTypeResult(PurgeDocumentTypeOutcome outcome) => Outcome = outcome;

    public PurgeDocumentTypeOutcome Outcome { get; }

    public static PurgeDocumentTypeResult Purged { get; } = new(PurgeDocumentTypeOutcome.Purged);

    public static PurgeDocumentTypeResult NotFound { get; } = new(PurgeDocumentTypeOutcome.NotFound);
}

/// <summary>
/// Elimina de forma permanente un tipo de documento y sus asociaciones de matriz
/// (requisitos, overrides de orden y precedencia OT). Los adjuntos de trámites
/// vivos conservan el archivo; el tipo deja de existir en el catálogo.
/// </summary>
public sealed class PurgeDocumentTypeHandler(IDocumentTypeRepository repository)
{
    private readonly IDocumentTypeRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<PurgeDocumentTypeResult> HandleAsync(
        PurgeDocumentTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return PurgeDocumentTypeResult.NotFound;
        }

        var purged = await _repository.PurgeAsync(command.Id, cancellationToken).ConfigureAwait(false);
        return purged ? PurgeDocumentTypeResult.Purged : PurgeDocumentTypeResult.NotFound;
    }
}
