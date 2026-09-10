using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Activate;

/// <summary>
/// Activa/reactiva una versión histórica (HU #11314, ADR-0042, §8 DT-7 del plan técnico). No re-lee ni
/// re-valida el binario: la versión ya pasó por el confirm (§7 DT-6) cuando entró al historial, así
/// que reactivarla es solo un cambio de estado. Repetible en cualquier orden: reactivar la ya activa es
/// un no-op idempotente (AC1). <c>WHERE tenant_id</c> explícito del repositorio: un id de otro tenant
/// es <see cref="ActivatePersonalizedDocumentVersionOutcome.NotFound"/> (negativo de aislamiento, AC6).
///
/// HU #11320 — auditoría a nivel de aplicación de la reactivación (complementa el trigger de BD).
/// </summary>
public sealed class ActivatePersonalizedDocumentVersionHandler
{
    private readonly ICompanyPersonalizedDocumentRepository _repository;
    private readonly ITenantSettingsRepository _settingsRepository;
    private readonly IAdminAuditWriter _auditWriter;
    private readonly IAuditContextAccessor _auditContext;

    public ActivatePersonalizedDocumentVersionHandler(
        ICompanyPersonalizedDocumentRepository repository,
        ITenantSettingsRepository settingsRepository,
        IAdminAuditWriter auditWriter,
        IAuditContextAccessor auditContext)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<ActivatePersonalizedDocumentVersionResult> HandleAsync(
        ActivatePersonalizedDocumentVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channelEnabled = await PersonalizedDocumentEligibilityGuard
            .IsWriteEnabledAsync(_settingsRepository, command.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!channelEnabled)
        {
            await AuditAsync(command, AuditVocabulary.Results.Failure, "canal_no_habilitado", cancellationToken)
                .ConfigureAwait(false);
            return ActivatePersonalizedDocumentVersionResult.ChannelNotEnabled();
        }

        var outcome = await _repository
            .ReactivateAsync(command.TenantId, command.Id, command.ActivatedBy, cancellationToken)
            .ConfigureAwait(false);

        var result = outcome.Outcome switch
        {
            PersonalizedDocumentReactivationOutcome.Reactivated =>
                ActivatePersonalizedDocumentVersionResult.Activated(outcome.Version!.Value),
            PersonalizedDocumentReactivationOutcome.InvalidStatus =>
                ActivatePersonalizedDocumentVersionResult.VersionNotActivable(),
            _ => ActivatePersonalizedDocumentVersionResult.NotFound(),
        };

        var errorCode = outcome.Outcome switch
        {
            PersonalizedDocumentReactivationOutcome.Reactivated => null,
            PersonalizedDocumentReactivationOutcome.InvalidStatus => "version_no_activable",
            _ => "not_found",
        };

        await AuditAsync(
            command,
            errorCode is null ? AuditVocabulary.Results.Success : AuditVocabulary.Results.Failure,
            errorCode,
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    // HU #11320 — actor + resultado; el superadmin que reactiva en un tenant ajeno queda registrado
    // con su propio ActorUserId (command.ActivatedBy).
    private async Task AuditAsync(
        ActivatePersonalizedDocumentVersionCommand command,
        string result,
        string? errorCode,
        CancellationToken cancellationToken) =>
        await _auditWriter.WriteAsync(
            new AdminAuditEntry(
                command.TenantId,
                TenantType: null,
                AuditVocabulary.Modules.Companies,
                EntityName: "personalized_document",
                AuditVocabulary.Operations.Activate,
                result,
                errorCode,
                ActorUserId: command.ActivatedBy,
                TargetEntityType: "PERSONALIZED_DOCUMENT",
                TargetEntityId: command.Id,
                _auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
}
