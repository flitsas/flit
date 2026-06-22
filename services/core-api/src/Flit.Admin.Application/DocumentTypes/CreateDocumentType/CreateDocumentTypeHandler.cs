using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.CreateDocumentType;

/// <summary>
/// Caso de uso de alta de un tipo de documento (HU #10193, AC1 / RF01).
///
/// Flujo: (1) normaliza y valida formato (codigo/nombre/descripcion) → 422 si falla;
/// (2) verifica unicidad global del código → 422 si ya existe; (3) crea con estado
/// activo. El flag <c>obligatorio</c> del request se ignora (no pertenece al catálogo).
/// </summary>
public sealed class CreateDocumentTypeHandler
{
    private readonly IDocumentTypeRepository _repository;

    public CreateDocumentTypeHandler(IDocumentTypeRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateDocumentTypeResult> HandleAsync(
        CreateDocumentTypeCommand command,
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
            return CreateDocumentTypeResult.Invalid(error);
        }

        if (await _repository.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return CreateDocumentTypeResult.Invalid(
                $"Ya existe un tipo de documento con el código '{code}'.");
        }

        var created = await _repository
            .CreateAsync(code, name, description, command.CreatedBy, cancellationToken)
            .ConfigureAwait(false);

        return CreateDocumentTypeResult.Success(DocumentTypeResponse.From(created));
    }
}
