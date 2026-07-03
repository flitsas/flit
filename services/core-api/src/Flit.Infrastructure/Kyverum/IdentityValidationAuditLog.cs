using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Kyverum;

/// <summary>
/// Persiste la bitácora del ciclo de validación de identidad en <c>tramites.identity_validation_audit</c>.
/// Usa un scope PROPIO y <c>SaveChanges</c> inmediato, de modo que la entrada queda registrada aunque el
/// flujo llamador falle o haga rollback (p.ej. webhook que devuelve 500/401). Traga sus propios errores: la
/// auditoría NUNCA rompe el flujo de negocio.
/// </summary>
internal sealed class IdentityValidationAuditLog(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityValidationAuditLog> logger) : IIdentityValidationAuditLog
{
    public async Task LogAsync(IdentityValidationAuditEntry e, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            var now = DateTimeOffset.UtcNow;

            db.IdentityValidationAudits.Add(new IdentityValidationAuditEvent
            {
                Id = Guid.NewGuid(),
                OccurredAt = now,
                Stage = e.Stage,
                Outcome = e.Outcome,
                TenantId = e.TenantId,
                ProcedureInstanceId = e.ProcedureInstanceId,
                ValidationId = e.ValidationId,
                KyverumVerificationId = e.KyverumVerificationId,
                PartyRole = e.PartyRole,
                HttpStatus = e.HttpStatus,
                SignaturePresent = e.SignaturePresent,
                SecretPresent = e.SecretPresent,
                DecryptOk = e.DecryptOk,
                ProviderStatus = e.ProviderStatus,
                ErrorType = e.ErrorType,
                Message = Truncate(e.Message, 1000),
                Detail = e.Detail,
                CreatedAt = now,
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            AuditLog.WriteFailed(logger, e.Stage, ex);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is not null && value.Length > max ? value[..max] : value;
}

/// <summary>Logging source-generated (CA1848) de la bitácora de identidad.</summary>
internal static partial class AuditLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo escribir la bitácora de identidad (stage={Stage}); no bloquea el flujo.")]
    public static partial void WriteFailed(ILogger logger, string stage, Exception ex);
}
