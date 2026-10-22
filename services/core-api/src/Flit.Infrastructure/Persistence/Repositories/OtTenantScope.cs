using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Queries.Domain.Time;
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
    public static TimeZoneInfo Bogota => ColombiaTime.Zone;

    private readonly FlitDbContext _context;
    private readonly IEffectiveTransitOfficeListResolver? _effectiveOffices;

    /// <param name="context">Contexto EF.</param>
    /// <param name="effectiveOffices">
    /// Bug #12912 — cálculo inverso de la lista efectiva (red Concesión / Marca Blanca). Sin él (repos
    /// construidos a mano en tests) el alcance es el criterio previo: solo grant propio habilitado.
    /// </param>
    public OtTenantScope(FlitDbContext context, IEffectiveTransitOfficeListResolver? effectiveOffices = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _effectiveOffices = effectiveOffices;
    }

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
                var tenantIds = await ListClientTenantIdsAsync(transitOfficeId.Value, cancellationToken)
                    .ConfigureAwait(false);

                return await action(transitOfficeId.Value, tenantIds).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Empresas cuyos trámites ve el organismo. Bug #12912 — con el resolver es la lista efectiva
    /// inversa (HU #12347): grant propio, hijas de una Concesión con grant y red Marca Blanca no
    /// bloqueada. Es el mismo conjunto que puede radicar en el organismo, así que no amplía lo visible
    /// más allá de lo que la red ya puede entregarle.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ListClientTenantIdsAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken)
    {
        if (_effectiveOffices is not null)
        {
            return await _effectiveOffices
                .ListEffectiveTenantIdsForOfficeAsync(transitOfficeId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _context.TenantTransitOfficeGrants
            .AsNoTracking()
            .Where(g => g.TransitOfficeId == transitOfficeId && g.IsEnabled)
            .Select(g => g.TenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compañías cuyo NOMBRE puede listar el organismo (Bug #12912, criterio Ley 1581 decidido por el
    /// humano). La regla vive en <see cref="OtVisibleCompanies"/>: grant directo respaldado por la red, y
    /// las que entran SOLO por la red únicamente si ya le entregaron algún trámite. Los conteos y filas de trámites no pasan por aquí:
    /// solo hay fila si hay trámite. Debe llamarse dentro de <see cref="ExecuteAsync{T}"/> (lectura
    /// cross-tenant ya abierta). Sin resolver, <paramref name="scopeTenantIds"/> ya son los grants
    /// directos y se devuelven tal cual.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListVisibleClientTenantIdsAsync(
        Guid transitOfficeId,
        IReadOnlyList<Guid> scopeTenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeTenantIds);
        if (_effectiveOffices is null)
        {
            return scopeTenantIds;
        }

        return await OtVisibleCompanies
            .FilterAsync(_context, transitOfficeId, scopeTenantIds, cancellationToken)
            .ConfigureAwait(false);
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
