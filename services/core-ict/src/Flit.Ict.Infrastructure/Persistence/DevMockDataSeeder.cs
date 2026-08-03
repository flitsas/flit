using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Ict.Infrastructure.Persistence;

/// <summary>
/// Seed de datos mock para desarrollo: logs de muestra, pre-trámites (uno atascado, uno con novedad,
/// uno procesado) y un webhook fallido, para que el submódulo frontend (Logs / Alertas ICT) muestre
/// contenido sin tener que correr todo el pipeline. Solo Development. Idempotente (no-op si ya hay logs).
/// </summary>
public sealed partial class DevMockDataSeeder(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<DevMockDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (await AlreadySeededAsync(connection, cancellationToken))
            {
                return;
            }

            var tenantId = await FirstTenantIdAsync(connection, cancellationToken);
            if (tenantId is null)
            {
                Log.NoTenant(logger);
                return;
            }

            // DbCommand crudo (NO ExecuteSqlRaw): el SQL contiene jsonb con llaves {..} que EF
            // interpretaría como placeholders de formato.
            await using var command = connection.CreateCommand();
            command.CommandText = MockSql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "tenant";
            parameter.Value = tenantId.Value;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
            Log.Seeded(logger);
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

    private static async Task<bool> AlreadySeededAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM ict.integration_log";
        var count = await cmd.ExecuteScalarAsync(ct);
        return count is long l && l > 0;
    }

    private static async Task<Guid?> FirstTenantIdAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM identity.tenants ORDER BY created_at LIMIT 1";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    private const string MockSql = """
        INSERT INTO ict.integration_log
            (tenant_id, log_type, direction, method, path, status_code, headers, request, response, correlation_id, duration_ms, usuario, created_at)
        VALUES
            (@tenant, 'auth', 'inbound', 'POST', '/api/v1/auth/login', 200, '{"content-type":"application/json","authorization":"***REDACTED***"}'::jsonb, '{"username":"ictdev","password":"***REDACTED***"}'::jsonb, '{"token":"***REDACTED***","expiresInSeconds":3600}'::jsonb, gen_random_uuid(), 58, 'ictdev', now() - interval '25 min'),
            (@tenant, 'transaction', 'inbound', 'POST', '/api/v1/external-transaction/register', 200, '{"content-type":"application/json","authorization":"***REDACTED***"}'::jsonb, '{"transaction_type":3,"company_manager_document":"*****8038","seller":[{"document_type":"CC","document_number":"****7262","name":"*******CANON"}]}'::jsonb, '{"TotalRows":1,"TotalRowsProcessed":1,"Detail":[{"Plate":"ABC123","Status":1,"Message":"registrado","TransactionFlit":"1042"}]}'::jsonb, gen_random_uuid(), 143, 'ictdev', now() - interval '22 min'),
            (@tenant, 'transaction', 'inbound', 'POST', '/api/v1/external-transaction/register', 422, '{"content-type":"application/json"}'::jsonb, '{"rows":21}'::jsonb, '{"error":"batch_limit_exceeded"}'::jsonb, gen_random_uuid(), 12, 'ictdev', now() - interval '20 min'),
            (@tenant, 'transaction', 'inbound', 'GET', '/api/v1/status-process/byId/MOCK-NOV-1', 200, '{"content-type":"application/json"}'::jsonb, NULL, '{"transactionFlit":"MOCK-NOV-1","statusValidation":4,"statusDescription":"Con Novedades"}'::jsonb, gen_random_uuid(), 9, 'ictdev', now() - interval '12 min'),
            (@tenant, 'external', 'outbound', 'GET', '/runt/vehicle/ABC123', 200, '{"x-source":"runt"}'::jsonb, NULL, '{"placa":"ABC123","estado":"ACTIVO","soatVigente":true}'::jsonb, gen_random_uuid(), 812, NULL, now() - interval '11 min'),
            (@tenant, 'webhook', 'outbound', 'POST', 'https://gestor.example.com/webhook', 500, '{"content-type":"application/json"}'::jsonb, '{"managerIdTransaction":"MOCK-NOV-1","ictEstado":"con_novedades","transactionType":3,"message":"CON NOVEDADES (mock)"}'::jsonb, NULL, gen_random_uuid(), 305, NULL, now() - interval '9 min'),
            (@tenant, 'transaction', 'inbound', 'PATCH', '/api/v1/pretramites', 200, '{"content-type":"application/json"}'::jsonb, '{"deliveryAddress":"Calle 1 # 2-3","rowVersion":3}'::jsonb, '{"updated":true,"rowVersion":4}'::jsonb, gen_random_uuid(), 27, 'ictdev', now() - interval '6 min'),
            (@tenant, 'auth', 'inbound', 'POST', '/api/v1/auth/login', 401, '{"content-type":"application/json"}'::jsonb, '{"username":"ictdev","password":"***REDACTED***"}'::jsonb, '{"error":"invalid_credentials"}'::jsonb, gen_random_uuid(), 33, NULL, now() - interval '3 min');

        INSERT INTO ict.external_integration_master
            (tenant_id, manager_id_transaction, transaction_type, transaction_operation, plate, process_status_id, business_validation, external_validation, created_at)
        VALUES
            (@tenant, 'MOCK-STUCK-1', 3, 1, 'ABC123', 2, 2, 1, now() - interval '95 min'),
            (@tenant, 'MOCK-NOV-1',   3, 1, 'DEF456', 4, 2, 2, now() - interval '28 min'),
            (@tenant, 'MOCK-OK-1',    1, 1, 'GHI789', 3, 2, 2, now() - interval '18 min');

        INSERT INTO ict.external_integration_webhook_master
            (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation, message_validation, ict_estado, is_notified, response_ok, created_at)
        SELECT id, tenant_id, manager_id_transaction, transaction_type, 4, 'CON NOVEDADES (mock)', 'con_novedades', true, false, now() - interval '14 min'
        FROM ict.external_integration_master
        WHERE manager_id_transaction = 'MOCK-NOV-1' AND tenant_id = @tenant
        LIMIT 1;
        """;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "ICT dev mock seed: logs, pre-trámites y webhook de muestra creados.")]
        public static partial void Seeded(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "ICT dev mock seed: no hay tenants; se omite.")]
        public static partial void NoTenant(ILogger logger);
    }
}
