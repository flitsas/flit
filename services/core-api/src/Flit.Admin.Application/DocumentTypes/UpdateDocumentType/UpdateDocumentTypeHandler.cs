using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.UpdateDocumentType;

/// <summary>
/// Caso de uso de actualización de un tipo de documento (HU #10193, AC3 / RF03).
///
/// Flujo: (1) valida nombre/descripcion → 422; (2) existencia → 404; (3) actualiza
/// name/description conservando el código de sistema. <c>codigo</c> del request se ignora.
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

        var name = command.Request.Nombre?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(command.Request.Descripcion)
            ? null
            : command.Request.Descripcion.Trim();

        var error = DocumentTypeValidator.ValidateNameAndDescription(name, description);
        if (error is not null)
        {
            return UpdateDocumentTypeResult.ValidationFailed(error);
        }

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return UpdateDocumentTypeResult.NotFound();
        }

        var code = existing.Code;

        var updated = await _repository
            .UpdateAsync(
                command.Id, code, name, description, command.UpdatedBy,
                command.Request.MimeTypesAllowed, command.Request.MaxSizeBytes,
                command.Request.EsAutogenerado, cancellationToken)
            .ConfigureAwait(false);

        // Carrera improbable: el registro fue borrado entre la lectura y el update.
        return updated is null
            ? UpdateDocumentTypeResult.NotFound()
            : UpdateDocumentTypeResult.Updated(DocumentTypeResponse.From(updated));
    }
}
