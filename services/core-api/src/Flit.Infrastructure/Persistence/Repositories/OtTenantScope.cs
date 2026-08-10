using Flit.Infrastructure.Analytics.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Resuelve qué puede ver un organismo de tránsito: su propio identificador a partir del tenant del
/// token, y las empresas con convenio vigente.
///
/// <para>Vive aparte a propósito. Es la única regla que separa los trámites de un organismo de los
/// de otro, y estaba escrita dentro del repositorio de métricas; en cuanto apareció un segundo
/// repositorio con el mismo eje invertido, copiarla habría creado dos definiciones de «lo que este
/// organismo puede ver» que se desincronizan en silencio — y ese desincronizado no se manifiesta
/// como un error, sino como datos de otra empresa en un reporte.</para>
///
/// <para>La lectura va bajo <c>SET LOCAL row_security = off</c> dentro de una transacción porque el
/// eje está invertido: RLS aísla por tenant y aquí se cruzan varios tenants a propósito, acotados
/// por el convenio con el organismo. El alcance lo pone entonces la cláusula de la consulta, no la
/// base — por eso esta clase es el sitio que hay que revisar si alguna vez se sospecha una fuga.</para>
/// </summary>
internal sealed class OtTenantScope
{
    public static TimeZoneInfo Bogota => ScheduleDueEvaluator.BogotaTimeZone;

    private readonly FlitDbContext _context;

    public OtTenantScope(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Ejecuta <paramref name="action"/> con el organismo resuelto y sus empresas. Devuelve
    /// <c>null</c> si el tenant no tiene organismo asociado.
    /// </summary>
    public async Task<T?> ExecuteAsync<T>(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        Func<Guid, IReadOnlyList<Guid>, Task<T>> action,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(action);

        var transitOfficeId = transitOfficeIdOverride is Guid overrideId && overrideId != Guid.Empty
            ? overrideId
            : await ResolveTransitOfficeIdAsync(otTenantId, cancellationToken).ConfigureAwait(false);

        if (transitOfficeId is null)
        {
            return null;
        }

        return await ReadCrossTenantAsync(
            async () =>
            {
                var tenantIds = await _context.TenantTransitOfficeGrants
                    .AsNoTracking()
                    .Where(g => g.TransitOfficeId == transitOfficeId.Value && g.IsEnabled)
                    .Select(g => g.TenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await action(transitOfficeId.Value, tenantIds).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid?> ResolveTransitOfficeIdAsync(
        Guid otTenantId,
        CancellationToken cancellationToken)
    {
        var profile = await ReadCrossTenantAsync(
            () => _context.TransitOfficeProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == otTenantId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return profile?.TransitOfficeId;
    }

    public async Task<T> ReadCrossTenantAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);

                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc cref="BogotaDays.Today"/>
    public static DateOnly TodayInBogota() => BogotaDays.Today();

    /// <inheritdoc cref="BogotaDays.Range"/>
    public static (DateTimeOffset From, DateTimeOffset To) DayRange(DateOnly from, DateOnly to) =>
        BogotaDays.Range(from, to);
}
