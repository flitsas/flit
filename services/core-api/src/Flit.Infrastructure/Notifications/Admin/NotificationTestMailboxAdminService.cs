using System.Text.RegularExpressions;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Notifications.Admin;

/// <summary>
/// Lectura y actualización de la fila única <c>admin.notification_test_settings</c>
/// (HU #11366, Feature #11349). Solo el buzón de pruebas — <see cref="NotificationTestSettingsRow.LastTestSentAt"/>
/// se lee pero nunca se escribe aquí; eso es de la HU #11368.
/// </summary>
internal sealed partial class NotificationTestMailboxAdminService : INotificationTestMailboxAdminService
{
    // Misma forma que WhitelistEmailValidator (Flit.Admin.Application.Companies.Whitelist): RFC 5321
    // simplificada, una arroba, sin espacios, dominio con punto. No se reutiliza esa clase porque es
    // `internal` de otro ensamblado sin InternalsVisibleTo hacia Flit.Infrastructure.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    private readonly FlitDbContext _db;
    private readonly IAdminAuditWriter _auditWriter;
    private readonly IAuditContextAccessor _auditContext;

    public NotificationTestMailboxAdminService(
        FlitDbContext db,
        IAdminAuditWriter auditWriter,
        IAuditContextAccessor auditContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<NotificationTestMailboxView> GetAsync(CancellationToken ct = default)
    {
        var row = await GetOrSeedRowAsync(ct).ConfigureAwait(false);
        return ToView(row);
    }

    public async Task<(NotificationTestMailboxWriteStatus Status, NotificationTestMailboxView? View)> UpdateRecipientAsync(
        UpdateNotificationTestMailboxRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim();
        if (!IsValidEmail(email))
        {
            await AuditAsync(userId, AuditVocabulary.Results.Failure, "correo_invalido", ct).ConfigureAwait(false);
            return (NotificationTestMailboxWriteStatus.InvalidEmail, null);
        }

        var row = await GetOrSeedRowAsync(ct).ConfigureAwait(false);

        if (request.RowVersion.HasValue && request.RowVersion.Value != row.RowVersion)
        {
            await AuditAsync(userId, AuditVocabulary.Results.Failure, "row_version_conflict", ct).ConfigureAwait(false);
            return (NotificationTestMailboxWriteStatus.Conflict, null);
        }

        // El valor anterior NO se registra en la auditoría (dato personal, Ley 1581): solo quién
        // cambió y que cambió — igual que CreatePersonalizedDocumentVersionHandler con el sha256.
        row.TestRecipientEmail = email;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = userId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync(userId, AuditVocabulary.Results.Success, null, ct).ConfigureAwait(false);

        return (NotificationTestMailboxWriteStatus.Ok, ToView(row));
    }

    private async Task<NotificationTestSettingsRow> GetOrSeedRowAsync(CancellationToken ct)
    {
        // La migración 67-HU11365 siembra exactamente una fila (índice único sobre expresión
        // constante). Este fallback solo protege un entorno de prueba donde esa migración no
        // corrió: nunca inserta una segunda fila junto a la sembrada.
        var row = await _db.NotificationTestSettings.SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is not null) return row;

        row = new NotificationTestSettingsRow
        {
            Id = Guid.NewGuid(),
            TestRecipientEmail = null,
            LastTestSentAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.NotificationTestSettings.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row;
    }

    private static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailPattern().IsMatch(email);

    private static NotificationTestMailboxView ToView(NotificationTestSettingsRow row) =>
        new(
            IsConfigured: row.TestRecipientEmail is not null,
            TestRecipientEmail: row.TestRecipientEmail,
            LastTestSentAt: row.LastTestSentAt,
            RowVersion: row.RowVersion);

    // El buzón de pruebas es dato personal (Ley 1581): la traza guarda quién cambió y el
    // resultado, nunca el valor de la dirección de correo.
    private Task AuditAsync(Guid? userId, string result, string? errorCode, CancellationToken ct) =>
        _auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                AuditVocabulary.Modules.Notifications,
                EntityName: "test_mailbox",
                AuditVocabulary.Operations.Update,
                result,
                errorCode,
                ActorUserId: userId,
                TargetEntityType: "NOTIFICATION_TEST_MAILBOX",
                TargetEntityId: null,
                _auditContext.ClientIp,
                UserAgent: null),
            ct);
}
