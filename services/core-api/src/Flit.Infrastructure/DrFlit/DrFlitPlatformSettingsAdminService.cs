using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.DrFlit;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Persistencia de <c>admin.dr_flit_settings</c> (HU #14771, Feature #14769).
/// </summary>
internal sealed class DrFlitPlatformSettingsAdminService : IDrFlitPlatformSettingsAdminService
{
    private readonly FlitDbContext _db;
    private readonly IAdminAuditWriter _auditWriter;
    private readonly IAuditContextAccessor _auditContext;

    public DrFlitPlatformSettingsAdminService(
        FlitDbContext db,
        IAdminAuditWriter auditWriter,
        IAuditContextAccessor auditContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<DrFlitPlatformSettingsView> GetAsync(CancellationToken ct = default)
    {
        var row = await GetOrSeedRowAsync(ct).ConfigureAwait(false);
        return ToView(row);
    }

    public async Task<bool> GetUiEnabledAsync(CancellationToken ct = default)
    {
        var row = await GetOrSeedRowAsync(ct).ConfigureAwait(false);
        return row.UiEnabled;
    }

    public async Task<(DrFlitPlatformSettingsWriteStatus Status, DrFlitPlatformSettingsView? View)> UpdateAsync(
        UpdateDrFlitPlatformSettingsRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var row = await GetOrSeedRowAsync(ct).ConfigureAwait(false);

        if (request.RowVersion.HasValue && request.RowVersion.Value != row.RowVersion)
        {
            await AuditAsync(userId, AuditVocabulary.Results.Failure, "row_version_conflict", ct).ConfigureAwait(false);
            return (DrFlitPlatformSettingsWriteStatus.Conflict, null);
        }

        row.UiEnabled = request.UiEnabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = userId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await AuditAsync(userId, AuditVocabulary.Results.Success, null, ct).ConfigureAwait(false);

        return (DrFlitPlatformSettingsWriteStatus.Ok, ToView(row));
    }

    private async Task<DrFlitSettingsRow> GetOrSeedRowAsync(CancellationToken ct)
    {
        var row = await _db.DrFlitSettings.SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is not null) return row;

        row = new DrFlitSettingsRow
        {
            Id = Guid.NewGuid(),
            UiEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.DrFlitSettings.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row;
    }

    private static DrFlitPlatformSettingsView ToView(DrFlitSettingsRow row) =>
        new(row.UiEnabled, row.RowVersion, row.UpdatedAt, row.UpdatedBy);

    private Task AuditAsync(Guid? userId, string result, string? errorCode, CancellationToken ct) =>
        _auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                AuditVocabulary.Modules.Config,
                EntityName: "platform_settings",
                AuditVocabulary.Operations.Update,
                result,
                errorCode,
                ActorUserId: userId,
                TargetEntityType: "DR_FLIT_SETTINGS",
                TargetEntityId: null,
                _auditContext.ClientIp,
                UserAgent: null),
            ct);
}
