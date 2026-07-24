using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.LegalRepresentatives.DeleteLegalRepresentative;

public enum DeleteLegalRepresentativeOutcome
{
    /// <summary>El representante existe y quedó inactivo (o ya lo estaba: idempotente).</summary>
    Deactivated,

    /// <summary>No existe un representante con ese id en el tenant.</summary>
    NotFound,
}

/// <summary>
/// Baja lógica (desactivación) de un representante legal (HU #10901). Idempotente: desactivar un
/// representante ya inactivo devuelve igualmente <see cref="DeleteLegalRepresentativeOutcome.Deactivated"/>.
/// Solo se responde <see cref="DeleteLegalRepresentativeOutcome.NotFound"/> cuando el id no existe en el
/// tenant.
/// </summary>
public sealed class DeleteLegalRepresentativeHandler
{
    private readonly ILegalRepresentativeReader _reader;
    private readonly ILegalRepresentativeRepository _repository;

    public DeleteLegalRepresentativeHandler(
        ILegalRepresentativeReader reader,
        ILegalRepresentativeRepository repository)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DeleteLegalRepresentativeOutcome> HandleAsync(
        DeleteLegalRepresentativeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _reader
            .GetByIdAsync(command.TenantId, command.Id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return DeleteLegalRepresentativeOutcome.NotFound;
        }

        // DeactivateAsync devuelve false si ya estaba inactivo; el resultado es idempotente igualmente.
        await _repository
            .DeactivateAsync(command.TenantId, command.Id, command.ChangedBy, cancellationToken)
            .ConfigureAwait(false);

        return DeleteLegalRepresentativeOutcome.Deactivated;
    }
}
