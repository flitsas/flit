using Flit.Admin.Application.Consolidados;
using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.Deeds.DeleteDeed;

public enum DeleteDeedOutcome
{
    /// <summary>La escritura existe y quedó dada de baja (o ya lo estaba: idempotente).</summary>
    Deleted,

    /// <summary>No existe una escritura con ese id en el tenant.</summary>
    NotFound,
}

/// <summary>
/// Baja lógica de una escritura (HU #10902). Idempotente por existencia: si la escritura ya estaba
/// inactiva sigue devolviendo <see cref="DeleteDeedOutcome.Deleted"/>; solo
/// <see cref="DeleteDeedOutcome.NotFound"/> cuando el id no existe en el tenant. No borra el PDF de
/// storage (lo recupera el ciclo de vida del file-manager); flit conserva la fila con la baja lógica.
/// </summary>
public sealed class DeleteDeedHandler
{
    private readonly IDeedReader _reader;
    private readonly IDeedRepository _repository;
    private readonly IConsolidadoInvalidacionMasiva? _invalidacion;

    /// <param name="invalidacion">
    /// HU #12789 — invalida en bloque los consolidados de la compañía. Opcional para no romper a los
    /// llamadores que no lo necesitan; en DI siempre se inyecta.
    /// </param>
    public DeleteDeedHandler(
        IDeedReader reader,
        IDeedRepository repository,
        IConsolidadoInvalidacionMasiva? invalidacion = null)
    {
        _invalidacion = invalidacion;
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DeleteDeedOutcome> HandleAsync(
        DeleteDeedCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _reader
            .GetByIdAsync(command.TenantId, command.Id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return DeleteDeedOutcome.NotFound;
        }

        // DeactivateAsync devuelve false si ya estaba inactiva; el resultado es idempotente.
        await _repository
            .DeactivateAsync(command.TenantId, command.Id, command.ChangedBy, cancellationToken)
            .ConfigureAwait(false);

        // HU #12789 AC2 — la escritura/RL entra al expediente: sus consolidados en curso quedan invalidados.
        if (_invalidacion is not null)
        {
            await _invalidacion.InvalidarPorCompaniaAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        }

        return DeleteDeedOutcome.Deleted;
    }
}
