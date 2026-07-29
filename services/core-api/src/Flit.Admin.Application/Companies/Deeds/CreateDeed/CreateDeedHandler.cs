using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.Deeds.CreateDeed;

/// <summary>
/// Alta de una escritura (HU #10902, ADR-0033): valida los metadatos, registra el documento en
/// storage vía <see cref="IDeedDocumentStorage"/> (presigned upload — el PDF va DIRECTO a S3) y
/// persiste la fila (solo path + hash) con el puente a compañías. El binario NUNCA pasa por el API ni
/// se guarda en BD; el cliente completa la subida con el <see cref="DeedUploadTicket"/> devuelto.
/// </summary>
public sealed class CreateDeedHandler
{
    private readonly IDeedDocumentStorage _storage;
    private readonly IDeedRepository _repository;

    public CreateDeedHandler(IDeedDocumentStorage storage, IDeedRepository repository)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateDeedResult> HandleAsync(
        CreateDeedCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = DeedValidation.ValidateMetadata(
            command.Description, command.VigenciaDesde, command.VigenciaHasta, command.RepresentedCompanyIds);

        if (string.IsNullOrWhiteSpace(command.Sha256))
        {
            errors.Add(new DeedValidationError(
                "sha256", "documento_requerido", "El hash del documento PDF es obligatorio."));
        }

        if (errors.Count > 0)
        {
            return CreateDeedResult.Invalid(errors);
        }

        // Registra el documento en storage ANTES del insert: se necesita el path para la fila. El
        // cliente sube el binario con el ticket devuelto; el SHA-256 lo aporta el cliente.
        var ticket = await _storage.CreateUploadAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

        var id = await _repository.CreateAsync(
            new SaveDeedData(
                command.TenantId,
                Id: null,
                command.Description!.Trim(),
                StoragePath: ticket.StoragePath,
                StorageSha256: command.Sha256!.Trim(),
                command.VigenciaDesde,
                command.VigenciaHasta,
                DeedValidation.NormalizeCompanies(command.RepresentedCompanyIds),
                command.CreatedBy,
                command.RepresentativeId),
            cancellationToken).ConfigureAwait(false);

        return CreateDeedResult.Success(id, ticket);
    }
}
