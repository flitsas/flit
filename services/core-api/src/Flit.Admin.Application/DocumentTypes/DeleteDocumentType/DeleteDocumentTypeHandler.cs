using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.DeleteDocumentType;

/// <summary>
/// Caso de uso de baja lógica de un tipo de documento (HU #10193, AC4/AC6 / RF04).
///
/// Flujo: (1) existencia → 404; (2) guard de integridad: si está referenciado en
/// <c>procedure_document_requirements</c> → 409 (no se desactiva); (3) soft-delete
/// (<c>is_active = false</c>), sin borrado físico.
/// </summary>
public sealed class DeleteDocumentTypeHandler
{
    private readonly IDocumentTypeRepository _repository;

    public DeleteDocumentTypeHandler(IDocumentTypeRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DeleteDocumentTypeResult> HandleAsync(
        DeleteDocumentTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return DeleteDocumentTypeResult.NotFound;
        }

        if (await _repository.HasActiveAssociationsAsync(command.Id, cancellationToken).ConfigureAwait(false))
        {
            // El bloqueo lo decide la existencia de asociaciones; los nombres de los trámites
            // son enriquecimiento best-effort para el 409 accionable de la consola.
            var associations = await _repository
                .GetAssociatedProcedureTypesAsync(command.Id, cancellationToken)
                .ConfigureAwait(false);

            return DeleteDocumentTypeResult.HasAssociations(associations);
        }

        var deleted = await _repository
            .SoftDeleteAsync(command.Id, command.DeletedBy, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? DeleteDocumentTypeResult.Deleted : DeleteDocumentTypeResult.NotFound;
    }
}
