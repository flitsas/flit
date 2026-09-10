using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments.Deactivate;

/// <summary>
/// «Volver al documento del sistema» (HU #11314, ADR-0042, §8 DT-7 del plan técnico): desactiva la
/// versión activa de un tipo SIN borrar ninguna fila ni archivo (restricción 9) — el histórico queda
/// intacto y cualquier versión se puede reactivar después. Es la vía por la que el cambio de canal a
/// <c>FLIT_SMTP</c> desactiva el reemplazo sin perder el historial (AC4): el interruptor ES la
/// vigencia de la versión, no un booleano nuevo (§8, "fuente única de verdad"). Idempotente.
///
/// HU #11320 — auditoría a nivel de aplicación de «volver al sistema» (complementa el trigger de BD).
/// </summary>
public sealed class DeactivatePersonalizedDocumentHandler
{
    private readonly ICompanyPersonalizedDocumentRepository _repository;
    private readonly ITenantSettingsRepository _settingsRepository;
    private readonly IAdminAuditWriter _auditWriter;
    private readonly IAuditContextAccessor _auditContext;

    public DeactivatePersonalizedDocumentHandler(
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

    public async Task<DeactivatePersonalizedDocumentResult> HandleAsync(
        DeactivatePersonalizedDocumentCommand command,
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
            return DeactivatePersonalizedDocumentResult.ChannelNotEnabled();
        }

        await _repository
            .DeactivateActiveAsync(command.TenantId, command.DocumentType, command.DeactivatedBy, cancellationToken)
            .ConfigureAwait(false);

        await AuditAsync(command, AuditVocabulary.Results.Success, null, cancellationToken).ConfigureAwait(false);

        return DeactivatePersonalizedDocumentResult.Deactivated();
    }

    // HU #11320 — actor + resultado; DocumentType (mandato|tramite_virtual) no es dato sensible.
    private async Task AuditAsync(
        DeactivatePersonalizedDocumentCommand command,
        string result,
        string? errorCode,
        CancellationToken cancellationToken) =>
        await _auditWriter.WriteAsync(
            new AdminAuditEntry(
                command.TenantId,
                TenantType: null,
                AuditVocabulary.Modules.Companies,
                EntityName: "personalized_document",
                AuditVocabulary.Operations.Deactivate,
                result,
                errorCode,
                ActorUserId: command.DeactivatedBy,
                TargetEntityType: "PERSONALIZED_DOCUMENT",
                TargetEntityId: null,
                _auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
}
