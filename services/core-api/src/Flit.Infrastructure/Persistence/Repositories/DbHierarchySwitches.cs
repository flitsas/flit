using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Queries.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación de <see cref="IHierarchySwitches"/> sobre <c>identity.hierarchy_switches</c>
/// (HU #12323, ADR-0057). Lectura <c>AsNoTracking</c> <b>por petición, sin caché</b>: apagar un
/// interruptor con un UPDATE (o con <c>PUT /api/v1/admin/platform/hierarchy-switches/{key}</c>)
/// cambia la siguiente petición sin despliegue ni nuevo token.
/// <list type="bullet">
///   <item>Fila ausente ⇒ <c>false</c> (apagado). Excepción ⇒ log Warning + <c>false</c>.
///   Fail-closed: apagado es el comportamiento previo al Feature (alcance <c>Single</c>).</item>
///   <item><see cref="SetAsync"/> solo acepta las dos claves conocidas; una clave desconocida
///   devuelve <c>null</c> y no crea filas.</item>
/// </list>
/// </summary>
internal sealed partial class DbHierarchySwitches : IHierarchySwitches
{
    private static readonly string[] KnownKeys =
    [
        HierarchySwitch.GroupReadScopeKey,
        HierarchySwitch.InheritedConfigurationKey,
    ];

    private readonly FlitDbContext _db;
    private readonly ILogger<DbHierarchySwitches> _logger;

    public DbHierarchySwitches(FlitDbContext db, ILogger<DbHierarchySwitches> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> IsGroupReadScopeEnabledAsync(CancellationToken cancellationToken = default) =>
        IsEnabledAsync(HierarchySwitch.GroupReadScopeKey, cancellationToken);

    public Task<bool> IsInheritedConfigurationEnabledAsync(CancellationToken cancellationToken = default) =>
        IsEnabledAsync(HierarchySwitch.InheritedConfigurationKey, cancellationToken);

    public async Task<IReadOnlyList<HierarchySwitchState>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.HierarchySwitches
            .AsNoTracking()
            .OrderBy(s => s.SwitchKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToState).ToList();
    }

    public async Task<HierarchySwitchState?> SetAsync(
        string key,
        bool isEnabled,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || !KnownKeys.Contains(key, StringComparer.Ordinal))
            return null;

        var row = await _db.HierarchySwitches
            .FirstOrDefaultAsync(s => s.SwitchKey == key, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return null;

        row.IsEnabled = isEnabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actorUserId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SwitchChanged(_logger, key, isEnabled, actorUserId);
        return ToState(row);
    }

    private async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var enabled = await _db.HierarchySwitches
                .AsNoTracking()
                .Where(s => s.SwitchKey == key)
                .Select(s => (bool?)s.IsEnabled)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (enabled is null)
            {
                SwitchMissing(_logger, key);
                return false;
            }

            return enabled.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SwitchReadFailed(_logger, key, ex);
            return false;
        }
    }

    private static HierarchySwitchState ToState(HierarchySwitch row) =>
        new(row.SwitchKey, row.IsEnabled, row.UpdatedAt, row.UpdatedBy);

    [LoggerMessage(
        EventId = 12323,
        Level = LogLevel.Warning,
        Message = "No se pudo leer el interruptor de jerarquía {SwitchKey}; se asume apagado (fail-closed).")]
    private static partial void SwitchReadFailed(ILogger logger, string switchKey, Exception ex);

    [LoggerMessage(
        EventId = 12324,
        Level = LogLevel.Warning,
        Message = "El interruptor de jerarquía {SwitchKey} no existe en identity.hierarchy_switches; se asume apagado (fail-closed).")]
    private static partial void SwitchMissing(ILogger logger, string switchKey);

    [LoggerMessage(
        EventId = 12325,
        Level = LogLevel.Information,
        Message = "Interruptor de jerarquía {SwitchKey} conmutado a {IsEnabled} por {ActorUserId}.")]
    private static partial void SwitchChanged(ILogger logger, string switchKey, bool isEnabled, Guid? actorUserId);
}
