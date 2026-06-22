using Flit.Admin.Domain.DocumentOrderOverrides;

namespace Flit.Admin.Application.DocumentOrderOverrides.UpdateDocumentOrderOverride;

/// <summary>
/// Caso de uso de reordenamiento de un override (HU #10198). Flujo: (1) valida
/// <c>orden</c> → 422; (2) actualiza el <c>sort_order</c> → 404 si el id no existe;
/// (3) devuelve el override actualizado. No re-valida la tupla porque no cambia: solo
/// el orden es mutable, así que no hay conflicto de unicidad posible.
/// </summary>
public sealed class UpdateDocumentOrderOverrideHandler
{
    private readonly IDocumentOrderOverrideRepository _repository;

    public UpdateDocumentOrderOverrideHandler(IDocumentOrderOverrideRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateDocumentOrderOverrideResult> HandleAsync(
        UpdateDocumentOrderOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var error = DocumentOrderOverrideValidator.Validate(command.Orden);
        if (error is not null)
        {
            return UpdateDocumentOrderOverrideResult.ValidationFailed(error);
        }

        var updated = await _repository
            .UpdateOrderAsync(command.Id, (short)command.Orden!.Value, cancellationToken)
            .ConfigureAwait(false);

        return updated is null
            ? UpdateDocumentOrderOverrideResult.NotFound
            : UpdateDocumentOrderOverrideResult.Updated(DocumentOrderOverrideResponse.From(updated));
    }
}
