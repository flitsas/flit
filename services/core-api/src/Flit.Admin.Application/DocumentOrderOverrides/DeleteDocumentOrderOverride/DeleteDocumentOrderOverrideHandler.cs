using Flit.Admin.Domain.DocumentOrderOverrides;

namespace Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;

/// <summary>
/// Caso de uso de borrado físico de un override (HU #10196, AC6 / RF11-12, RF15-16).
/// Flujo: (1) existencia → 404; (2) borrado físico de la fila. Tras el borrado, la matriz
/// resuelta vuelve al nivel inmediatamente inferior (OT o Default).
/// </summary>
public sealed class DeleteDocumentOrderOverrideHandler
{
    private readonly IDocumentOrderOverrideRepository _repository;

    public DeleteDocumentOrderOverrideHandler(IDocumentOrderOverrideRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DeleteDocumentOrderOverrideResult> HandleAsync(
        DeleteDocumentOrderOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var deleted = await _repository.DeleteAsync(command.Id, cancellationToken).ConfigureAwait(false);

        return deleted
            ? DeleteDocumentOrderOverrideResult.Deleted
            : DeleteDocumentOrderOverrideResult.NotFound;
    }
}
