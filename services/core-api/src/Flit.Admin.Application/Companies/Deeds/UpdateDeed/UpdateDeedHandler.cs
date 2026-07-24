using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.Deeds.UpdateDeed;

/// <summary>
/// Edición de una escritura (HU #10902, ADR-0033): valida los metadatos y persiste descripción,
/// vigencia y compañías. Si el cliente envía un <c>Sha256</c> nuevo, reemplaza el PDF registrando un
/// artefacto nuevo en storage (presigned upload) y devuelve el ticket; si no, conserva el artefacto
/// actual. Devuelve <see cref="UpdateDeedOutcome.NotFound"/> si la escritura no existe en el tenant.
/// </summary>
public sealed class UpdateDeedHandler
{
    private readonly IDeedDocumentStorage _storage;
    private readonly IDeedRepository _repository;

    public UpdateDeedHandler(IDeedDocumentStorage storage, IDeedRepository repository)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateDeedResult> HandleAsync(
        UpdateDeedCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = DeedValidation.ValidateMetadata(
            command.Description, command.VigenciaDesde, command.VigenciaHasta, command.RepresentedCompanyIds);

        if (errors.Count > 0)
        {
            return UpdateDeedResult.Invalid(errors);
        }

        // Reemplazo opcional del PDF: solo si el cliente aporta un hash nuevo.
        var replacesArtifact = !string.IsNullOrWhiteSpace(command.Sha256);
        DeedUploadTicket? ticket = null;
        if (replacesArtifact)
        {
            ticket = await _storage.CreateUploadAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        }

        var updated = await _repository.UpdateAsync(
            new SaveDeedData(
                command.TenantId,
                command.Id,
                command.Description!.Trim(),
                StoragePath: ticket?.StoragePath,
                StorageSha256: replacesArtifact ? command.Sha256!.Trim() : null,
                command.VigenciaDesde,
                command.VigenciaHasta,
                DeedValidation.NormalizeCompanies(command.RepresentedCompanyIds),
                command.UpdatedBy),
            cancellationToken).ConfigureAwait(false);

        return updated ? UpdateDeedResult.Updated(ticket) : UpdateDeedResult.NotFound();
    }
}
