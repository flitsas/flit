using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;

/// <summary>
/// Caso de uso del alta de un grant de organismo de tránsito (HU #10192, AC2).
///
/// Flujo: (1) valida que el <c>transitOfficeId</c> exista en el catálogo estático —
/// si no, retorna 422 sin tocar BD; (2) delega al repositorio el alta + audit en una
/// única transacción. Idempotente: si el grant ya existe no duplica fila ni auditoría
/// (se reporta vía <see cref="AddTransitGrantResult.Added"/> = false).
/// </summary>
public sealed class AddTransitGrantHandler
{
    private readonly ITransitOfficeCatalog _catalog;
    private readonly ITransitGrantRepository _repository;

    public AddTransitGrantHandler(ITransitOfficeCatalog catalog, ITransitGrantRepository repository)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AddTransitGrantResult> HandleAsync(
        AddTransitGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // AC2: el organismo debe existir en el catálogo estático.
        if (command.TransitOfficeId == Guid.Empty || !_catalog.Exists(command.TransitOfficeId))
        {
            return AddTransitGrantResult.Invalid(
            [
                new TransitGrantValidationError(
                    "transitOfficeId",
                    "El organismo de tránsito no existe en el catálogo.",
                    command.TransitOfficeId == Guid.Empty ? null : command.TransitOfficeId.ToString()),
            ]);
        }

        var added = await _repository
            .AddGrantAsync(
                command.TenantId, command.TransitOfficeId, command.CreatedBy, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        return AddTransitGrantResult.Success(added);
    }
}
