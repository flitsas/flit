using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Activate;

/// <summary>
/// Activa/reactiva una versión histórica (HU #11314, ADR-0042, §8 DT-7 del plan técnico). No re-lee ni
/// re-valida el binario: la versión ya pasó por el confirm (§7 DT-6) cuando entró al historial, así
/// que reactivarla es solo un cambio de estado. Repetible en cualquier orden: reactivar la ya activa es
/// un no-op idempotente (AC1). <c>WHERE tenant_id</c> explícito del repositorio: un id de otro tenant
/// es <see cref="ActivatePersonalizedDocumentVersionOutcome.NotFound"/> (negativo de aislamiento, AC6).
/// </summary>
public sealed class ActivatePersonalizedDocumentVersionHandler
{
    private readonly ICompanyPersonalizedDocumentRepository _repository;
    private readonly ITenantSettingsRepository _settingsRepository;

    public ActivatePersonalizedDocumentVersionHandler(
        ICompanyPersonalizedDocumentRepository repository,
        ITenantSettingsRepository settingsRepository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
    }

    public async Task<ActivatePersonalizedDocumentVersionResult> HandleAsync(
        ActivatePersonalizedDocumentVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channelEnabled = await PersonalizedDocumentChannelGuard
            .IsWriteEnabledAsync(_settingsRepository, command.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!channelEnabled)
        {
            return ActivatePersonalizedDocumentVersionResult.ChannelNotEnabled();
        }

        var outcome = await _repository
            .ReactivateAsync(command.TenantId, command.Id, command.ActivatedBy, cancellationToken)
            .ConfigureAwait(false);

        return outcome.Outcome switch
        {
            PersonalizedDocumentReactivationOutcome.Reactivated =>
                ActivatePersonalizedDocumentVersionResult.Activated(outcome.Version!.Value),
            PersonalizedDocumentReactivationOutcome.InvalidStatus =>
                ActivatePersonalizedDocumentVersionResult.VersionNotActivable(),
            _ => ActivatePersonalizedDocumentVersionResult.NotFound(),
        };
    }
}
