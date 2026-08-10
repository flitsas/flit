using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Confirm;

/// <summary>
/// Confirmación de una versión (HU #11313, ADR-0042, §7 DT-6 del plan técnico). Única oportunidad de
/// ver los bytes: re-lee el objeto de storage, recalcula el SHA-256 y valida el PDF
/// (<see cref="PdfIntegrityValidator"/>). Solo al pasar todo, la versión pasa a <c>activo</c> y la
/// anterior a <c>historico</c> (AC2); si algo falla, la versión queda en <c>rechazado</c> (AC3) y NO
/// se toca la activa previa. <c>GetByIdAsync</c> filtra <c>tenant_id</c> explícitamente: un id de otro
/// tenant devuelve <see cref="ConfirmPersonalizedDocumentVersionOutcome.NotFound"/> (aislamiento, AC5).
/// </summary>
public sealed class ConfirmPersonalizedDocumentVersionHandler
{
    private readonly ICompanyPersonalizedDocumentStorage _storage;
    private readonly ICompanyPersonalizedDocumentRepository _repository;
    private readonly ITenantSettingsRepository _settingsRepository;
    private readonly PdfIntegrityValidator _validator;

    public ConfirmPersonalizedDocumentVersionHandler(
        ICompanyPersonalizedDocumentStorage storage,
        ICompanyPersonalizedDocumentRepository repository,
        ITenantSettingsRepository settingsRepository,
        PdfIntegrityValidator validator)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ConfirmPersonalizedDocumentVersionResult> HandleAsync(
        ConfirmPersonalizedDocumentVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channelEnabled = await PersonalizedDocumentChannelGuard
            .IsWriteEnabledAsync(_settingsRepository, command.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!channelEnabled)
        {
            return ConfirmPersonalizedDocumentVersionResult.ChannelNotEnabled();
        }

        // WHERE tenant_id explícito: un id de otro tenant no existe para este caller (AC5).
        var record = await _repository
            .GetByIdAsync(command.TenantId, command.Id, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return ConfirmPersonalizedDocumentVersionResult.NotFound();
        }

        using var stream = await _storage
            .OpenReadAsync(record.StoragePath, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            return await RejectAsync(command, "file", "pdf_ilegible", "El objeto subido no se pudo leer.", cancellationToken)
                .ConfigureAwait(false);
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        var check = _validator.Validate(bytes, record.StorageSha256);
        if (!check.IsValid)
        {
            return await RejectAsync(command, "file", check.ErrorCode!, check.ErrorMessage!, cancellationToken)
                .ConfigureAwait(false);
        }

        await _repository.ActivateAsync(
            command.TenantId,
            command.Id,
            new ConfirmCompanyPersonalizedDocumentData(check.Sha256!, bytes.LongLength, check.PageCount, command.ConfirmedBy),
            cancellationToken).ConfigureAwait(false);

        return ConfirmPersonalizedDocumentVersionResult.Activated(record.Version, check.Sha256!, check.PageCount);
    }

    private async Task<ConfirmPersonalizedDocumentVersionResult> RejectAsync(
        ConfirmPersonalizedDocumentVersionCommand command,
        string field,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        await _repository.RejectAsync(command.TenantId, command.Id, cancellationToken).ConfigureAwait(false);
        return ConfirmPersonalizedDocumentVersionResult.Rejected([new PersonalizedDocumentValidationError(field, code, message)]);
    }
}
