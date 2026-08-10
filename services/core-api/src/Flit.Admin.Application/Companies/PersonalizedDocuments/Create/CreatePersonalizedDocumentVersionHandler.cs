using Flit.Admin.Domain.Companies.PersonalizedDocuments;
using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Create;

/// <summary>
/// Alta de una versión de documento personalizado (HU #11313, ADR-0042). Orden de comprobaciones,
/// de más barata a más cara: (1) tipo válido (AC1, 400), (2) canal habilitado (AC4, 409
/// <c>canal_no_habilitado</c> — fuente única <see cref="ITenantSettingsRepository"/>), (3) metadatos
/// (422), y solo entonces registra el ticket de subida en storage y persiste la fila en
/// <c>pendiente</c> (AC2: ninguna carga borra la anterior — la versión es siempre la siguiente para
/// <c>(tenant, tipo)</c>).
/// </summary>
public sealed class CreatePersonalizedDocumentVersionHandler
{
    private readonly ICompanyPersonalizedDocumentStorage _storage;
    private readonly ICompanyPersonalizedDocumentRepository _repository;
    private readonly ITenantSettingsRepository _settingsRepository;

    public CreatePersonalizedDocumentVersionHandler(
        ICompanyPersonalizedDocumentStorage storage,
        ICompanyPersonalizedDocumentRepository repository,
        ITenantSettingsRepository settingsRepository)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
    }

    public async Task<CreatePersonalizedDocumentVersionResult> HandleAsync(
        CreatePersonalizedDocumentVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!PersonalizedDocumentTypes.IsValid(command.DocumentType))
        {
            return CreatePersonalizedDocumentVersionResult.InvalidType(new PersonalizedDocumentValidationError(
                "documentType", "tipo_documento_invalido",
                "El tipo de documento debe ser 'mandato' o 'tramite_virtual'."));
        }

        var channelEnabled = await PersonalizedDocumentChannelGuard
            .IsWriteEnabledAsync(_settingsRepository, command.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!channelEnabled)
        {
            return CreatePersonalizedDocumentVersionResult.ChannelNotEnabled();
        }

        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return CreatePersonalizedDocumentVersionResult.Invalid(errors);
        }

        var version = await _repository
            .GetNextVersionAsync(command.TenantId, command.DocumentType!, cancellationToken)
            .ConfigureAwait(false);

        var ticket = await _storage
            .CreateUploadAsync(command.TenantId, command.DocumentType!, cancellationToken)
            .ConfigureAwait(false);

        var id = await _repository.CreatePendingAsync(
            new SaveCompanyPersonalizedDocumentData(
                command.TenantId,
                command.DocumentType!,
                version,
                command.Filename!.Trim(),
                ticket.StoragePath,
                command.Sha256!.Trim(),
                command.SizeBytes,
                command.CreatedBy),
            cancellationToken).ConfigureAwait(false);

        return CreatePersonalizedDocumentVersionResult.Created(id, version, ticket);
    }

    private static List<PersonalizedDocumentValidationError> Validate(CreatePersonalizedDocumentVersionCommand command)
    {
        var errors = new List<PersonalizedDocumentValidationError>();

        if (string.IsNullOrWhiteSpace(command.Filename))
        {
            errors.Add(new PersonalizedDocumentValidationError("filename", "requerido", "El nombre del archivo es obligatorio."));
        }

        if (string.IsNullOrWhiteSpace(command.Sha256))
        {
            errors.Add(new PersonalizedDocumentValidationError("sha256", "documento_requerido", "El hash del documento PDF es obligatorio."));
        }

        if (command.SizeBytes <= 0)
        {
            errors.Add(new PersonalizedDocumentValidationError("sizeBytes", "requerido", "El tamaño del archivo es obligatorio."));
        }
        else if (command.SizeBytes > PdfIntegrityValidator.MaxSizeBytes)
        {
            errors.Add(new PersonalizedDocumentValidationError("sizeBytes", "excede_tamano", "El tamaño declarado excede el máximo permitido (20 MB)."));
        }

        return errors;
    }
}
