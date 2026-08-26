using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Resuelve qué compañías puede ver SuperAdmin en modo «todas las compañías»: las gestoras activas
/// de la plataforma, sin incluir organismos de tránsito (que también son filas de <c>identity.tenants</c>
/// pero no tramitan como cliente).
///
/// <para>Vive aparte y no reutiliza <see cref="OtTenantScope"/> a propósito, aunque el mecanismo de
/// bypass sea el mismo: <see cref="OtTenantScope"/> es el sitio marcado como sensible para el eje
/// «organismo mirando varias empresas», y tocarlo para un eje distinto —SuperAdmin mirando todas las
/// compañías— arriesgaría ese flujo por una necesidad que no tiene nada que ver. Son dos ejes cruzados
/// distintos con la misma forma de bypass, no el mismo eje.</para>
///
/// <para>La lectura va bajo <c>SET LOCAL row_security = off</c> dentro de una transacción por la
/// misma razón que en <see cref="OtTenantScope"/>: <c>procedure_instances</c> tiene RLS por tenant, y
/// aquí se cruzan todas las compañías a propósito. Por eso esta clase es el sitio que hay que revisar
/// si alguna vez se sospecha una fuga en el motor de SuperAdmin.</para>
/// </summary>
internal sealed class SuperAdminTenantScope
{
    private readonly FlitDbContext _context;

    public SuperAdminTenantScope(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Ejecuta <paramref name="action"/> con la lista de compañías activas resuelta, dentro del
    /// bypass de RLS.
    /// </summary>
    public Task<T> ExecuteAsync<T>(
        Func<IReadOnlyList<Guid>, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        return ReadCrossTenantAsync(
            async () =>
            {
                var tenantIds = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => t.IsActive && !_context.TransitOfficeProfiles.Any(p => p.TenantId == t.Id))
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await action(tenantIds).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private async Task<T> ReadCrossTenantAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
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
}
