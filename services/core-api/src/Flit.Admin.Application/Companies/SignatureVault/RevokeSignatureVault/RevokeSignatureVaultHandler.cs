using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.RevokeSignatureVault;

public enum RevokeSignatureVaultOutcome
{
    /// <summary>La firma existe y quedó revocada (o ya lo estaba: idempotente).</summary>
    Revoked,

    /// <summary>No existe una firma con ese id en el tenant.</summary>
    NotFound,
}

/// <summary>
/// Revoca una firma del baúl (baja lógica, ADR-0025 §5). Idempotente: revocar una firma ya revocada
/// vuelve a devolver <see cref="RevokeSignatureVaultOutcome.Revoked"/>. Solo se responde
/// <see cref="RevokeSignatureVaultOutcome.NotFound"/> cuando el id no existe dentro del tenant.
/// </summary>
public sealed class RevokeSignatureVaultHandler
{
    private readonly ISignatureVaultReader _reader;
    private readonly ISignatureVaultRepository _repository;

    public RevokeSignatureVaultHandler(
        ISignatureVaultReader reader,
        ISignatureVaultRepository repository)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<RevokeSignatureVaultOutcome> HandleAsync(
        RevokeSignatureVaultCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _reader
            .GetByIdAsync(command.TenantId, command.Id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return RevokeSignatureVaultOutcome.NotFound;
        }

        // RevokeAsync devuelve false si ya estaba revocada; el resultado es idempotente igualmente.
        await _repository.RevokeAsync(
            new RevokeSignatureVaultData(command.Id, command.TenantId, command.ChangedBy, command.CorrelationId),
            cancellationToken).ConfigureAwait(false);

        return RevokeSignatureVaultOutcome.Revoked;
    }
}
