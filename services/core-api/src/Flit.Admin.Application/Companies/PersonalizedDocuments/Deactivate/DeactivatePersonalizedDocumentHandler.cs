using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Deactivate;

/// <summary>
/// «Volver al documento del sistema» (HU #11314, ADR-0042, §8 DT-7 del plan técnico): desactiva la
/// versión activa de un tipo SIN borrar ninguna fila ni archivo (restricción 9) — el histórico queda
/// intacto y cualquier versión se puede reactivar después. Es la vía por la que el cambio de canal a
/// <c>FLIT_SMTP</c> desactiva el reemplazo sin perder el historial (AC4): el interruptor ES la
/// vigencia de la versión, no un booleano nuevo (§8, "fuente única de verdad"). Idempotente.
/// </summary>
public sealed class DeactivatePersonalizedDocumentHandler
{
    private readonly ICompanyPersonalizedDocumentRepository _repository;
    private readonly ITenantSettingsRepository _settingsRepository;

    public DeactivatePersonalizedDocumentHandler(
        ICompanyPersonalizedDocumentRepository repository,
        ITenantSettingsRepository settingsRepository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
    }

    public async Task<DeactivatePersonalizedDocumentResult> HandleAsync(
        DeactivatePersonalizedDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channelEnabled = await PersonalizedDocumentChannelGuard
            .IsWriteEnabledAsync(_settingsRepository, command.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!channelEnabled)
        {
            return DeactivatePersonalizedDocumentResult.ChannelNotEnabled();
        }

        await _repository
            .DeactivateActiveAsync(command.TenantId, command.DocumentType, command.DeactivatedBy, cancellationToken)
            .ConfigureAwait(false);

        return DeactivatePersonalizedDocumentResult.Deactivated();
    }
}
