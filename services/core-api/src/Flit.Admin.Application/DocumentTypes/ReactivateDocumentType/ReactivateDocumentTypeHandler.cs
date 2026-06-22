using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.ReactivateDocumentType;

/// <summary>
/// Caso de uso de reactivación de un tipo de documento (contraparte del soft-delete,
/// AC4). Vuelve <c>is_active = true</c> para que el catálogo y la asociación a trámites
/// vuelvan a ofrecerlo.
///
/// Flujo: (1) existencia → 404; (2) reactivación (<c>is_active = true</c>). No hay
/// conflicto de código posible: la unicidad es global (activos e inactivos), así que
/// el código del registro reactivado nunca colisiona con otro. La operación es
/// idempotente: reactivar un documento ya activo devuelve 204 igualmente.
/// </summary>
public sealed class ReactivateDocumentTypeHandler
{
    private readonly IDocumentTypeRepository _repository;

    public ReactivateDocumentTypeHandler(IDocumentTypeRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ReactivateDocumentTypeResult> HandleAsync(
        ReactivateDocumentTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reactivated = await _repository
            .ReactivateAsync(command.Id, command.UpdatedBy, cancellationToken)
            .ConfigureAwait(false);

        return reactivated
            ? ReactivateDocumentTypeResult.Reactivated
            : ReactivateDocumentTypeResult.NotFound;
    }
}
