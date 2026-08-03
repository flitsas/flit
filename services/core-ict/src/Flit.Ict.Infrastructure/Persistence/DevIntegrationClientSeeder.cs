using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Ict.Infrastructure.Persistence;

/// <summary>
/// Seed de desarrollo: crea un cliente de integración de prueba (username <c>ictdev</c> / password
/// <c>IctDev2026!</c>) enlazado al primer tenant existente, para poder probar el login ICT en local.
/// Solo en Development. Idempotente. No-op si no hay tenants.
/// </summary>
public sealed partial class DevIntegrationClientSeeder(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<DevIntegrationClientSeeder> logger) : IHostedService
{
    private const string DevUsername = "ictdev";
    private const string DevPassword = "IctDev2026!";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IIctPasswordHasher>();

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var tenantId = await FirstTenantIdAsync(connection, cancellationToken);
            if (tenantId is null)
            {
                Log.NoTenant(logger);
                return;
            }

            var hash = hasher.Hash(DevPassword);
            // El índice único es funcional: uq_integration_clients_username_lower ON (lower(username)).
            // ON CONFLICT (username) falla con 42P10; hay que arbitrar por la expresión del índice.
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ict.integration_clients (tenant_id, username, password_hash, scopes, is_active)
                    VALUES ({tenantId.Value}, {DevUsername}, {hash},
                            '["ict.transactions.write","ict.status.read"]'::jsonb, true)
                    ON CONFLICT ((lower(username))) DO NOTHING
                    """, cancellationToken);
                Log.Seeded(logger, DevUsername);
            }
            catch (Exception ex)
            {
                // No tumbar el host: el cliente de prueba es solo DX local.
                Log.SeedFailed(logger, ex);
            }
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<Guid?> FirstTenantIdAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM identity.tenants ORDER BY created_at LIMIT 1";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "ICT dev seed: cliente de integración '{Username}' listo.")]
        public static partial void Seeded(ILogger logger, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "ICT dev seed: no hay tenants en identity.tenants; se omite el seed del cliente de prueba.")]
        public static partial void NoTenant(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "ICT dev seed: falló el upsert del cliente de prueba; se continúa sin él.")]
        public static partial void SeedFailed(ILogger logger, Exception exception);
    }
}
