using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;

/// <summary>
/// Caso de uso de la baja de un grant de organismo de tránsito (HU #10192, AC3).
/// Delega al repositorio el borrado + audit en una única transacción. Devuelve
/// <c>false</c> si el grant no existía (→ 404).
/// </summary>
public sealed class RemoveTransitGrantHandler
{
    private readonly ITransitGrantRepository _repository;

    public RemoveTransitGrantHandler(ITransitGrantRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<bool> HandleAsync(
        RemoveTransitGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _repository
            .RemoveGrantAsync(
                command.TenantId, command.TransitOfficeId, command.ChangedBy, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
    }
}
