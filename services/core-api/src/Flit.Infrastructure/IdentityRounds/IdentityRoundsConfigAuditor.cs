using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Tramites.Application.Identity.Rounds;
using Flit.Tramites.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.IdentityRounds;

/// <summary>
/// Auditoría de los cambios del configurador global de rondas en el rastro administrativo unificado (<see cref="IAdminAuditWriter"/>,
/// módulo Config, global de plataforma: sin tenant). Deja quién (actor), cuándo (<c>changed_at</c>) y los valores anterior y nuevo. Los
/// correos de alerta son datos de contacto de operación (no de clientes). Nunca rompe el guardado: si la auditoría falla se registra solo el tipo.
/// </summary>
internal sealed partial class IdentityRoundsConfigAuditor(
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext,
    ILogger<IdentityRoundsConfigAuditor> logger) : IIdentityRoundsConfigAuditor
{
    public const string EntityName = "identity_rounds_config";
    public const string TargetEntityType = "IDENTITY_ROUNDS_CONFIG";

    public async Task WriteAsync(Guid? actorUserId, IdentityRoundsConfig before, IdentityRoundsConfig after, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        try
        {
            await auditWriter.WriteAsync(
                new AdminAuditEntry(
                    TenantId: null,
                    TenantType: null,
                    AuditVocabulary.Modules.Config,
                    EntityName,
                    AuditVocabulary.Operations.Update,
                    AuditVocabulary.Results.Success,
                    ErrorCode: null,
                    ActorUserId: actorUserId ?? auditContext.UserId,
                    TargetEntityType,
                    TargetEntityId: null,
                    auditContext.ClientIp,
                    UserAgent: null,
                    OldValue: Serialize(before),
                    NewValue: Serialize(after)),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuditoriaFallida(logger, ex.GetType().Name);
        }
    }

    internal static string Serialize(IdentityRoundsConfig c) =>
        JsonSerializer.Serialize(new { rondasMaximas = c.RondasMaximas, disparadorAlerta = c.DisparadorAlerta, correosAlerta = c.CorreosAlerta });

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo escribir la auditoría del cambio de la configuración de rondas de identidad ({ErrorType}).")]
    private static partial void AuditoriaFallida(ILogger logger, string errorType);
}
