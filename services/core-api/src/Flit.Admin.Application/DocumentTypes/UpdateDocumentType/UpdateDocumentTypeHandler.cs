using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.UpdateDocumentType;

/// <summary>
/// Caso de uso de actualización de un tipo de documento (HU #10193, AC3 / RF03).
///
/// Flujo: (1) valida formato → 422; (2) verifica existencia → 404; (3) unicidad del
/// código excluyendo el propio id → 422; (4) actualiza code/name/description y sella
/// <c>updated_by/updated_at</c>.
/// </summary>
public sealed class UpdateDocumentTypeHandler
{
    private readonly IDocumentTypeRepository _repository;

    public UpdateDocumentTypeHandler(IDocumentTypeRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateDocumentTypeResult> HandleAsync(
        UpdateDocumentTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Request.Codigo?.Trim() ?? string.Empty;
        var name = command.Request.Nombre?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(command.Request.Descripcion)
            ? null
            : command.Request.Descripcion.Trim();

        var error = DocumentTypeValidator.Validate(code, name, description);
        if (error is not null)
        {
            return UpdateDocumentTypeResult.ValidationFailed(error);
        }

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return UpdateDocumentTypeResult.NotFound();
        }

        if (await _repository.CodeExistsAsync(code, command.Id, cancellationToken).ConfigureAwait(false))
        {
            return UpdateDocumentTypeResult.ValidationFailed(
                $"Ya existe un tipo de documento con el código '{code}'.");
        }

        var updated = await _repository
            .UpdateAsync(command.Id, code, name, description, command.UpdatedBy, cancellationToken)
            .ConfigureAwait(false);

        // Carrera improbable: el registro fue borrado entre la lectura y el update.
        return updated is null
            ? UpdateDocumentTypeResult.NotFound()
            : UpdateDocumentTypeResult.Updated(DocumentTypeResponse.From(updated));
    }
}
