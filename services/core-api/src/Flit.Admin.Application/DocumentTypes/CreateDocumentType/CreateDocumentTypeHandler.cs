using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes.CreateDocumentType;

/// <summary>
/// Caso de uso de alta de un tipo de documento (HU #10193, AC1 / RF01).
///
/// Flujo: (1) valida nombre/descripcion → 422; (2) genera un código único a partir
/// del nombre (ignora <c>codigo</c> del cliente); (3) crea con estado activo.
/// El flag <c>obligatorio</c> del request se ignora (no pertenece al catálogo).
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

        var name = command.Request.Nombre?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(command.Request.Descripcion)
            ? null
            : command.Request.Descripcion.Trim();

        var error = DocumentTypeValidator.ValidateNameAndDescription(name, description);
        if (error is not null)
        {
            return CreateDocumentTypeResult.Invalid(error);
        }

        var code = await DocumentTypeCodeFactory
            .AllocateUniqueAsync(
                name,
                (candidate, ct) => _repository.CodeExistsAsync(candidate, null, ct),
                cancellationToken)
            .ConfigureAwait(false);

        var created = await _repository
            .CreateAsync(
                code, name, description, command.CreatedBy,
                command.Request.MimeTypesAllowed, command.Request.MaxSizeBytes,
                command.Request.EsAutogenerado == true, cancellationToken)
            .ConfigureAwait(false);

        return CreateDocumentTypeResult.Success(DocumentTypeResponse.From(created));
    }
}
