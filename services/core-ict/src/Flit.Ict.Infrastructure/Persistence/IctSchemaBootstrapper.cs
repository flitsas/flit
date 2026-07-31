using System.Data;
using Flit.Ict.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Persistence;

/// <summary>Opciones del bootstrap de base de datos de core-ict.</summary>
public sealed class IctDatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Aplica el DDL embebido al arrancar (idempotente). Default true.</summary>
    public bool AutoMigrate { get; init; } = true;
}

/// <summary>
/// Aplica el DDL crudo embebido del schema <c>ict</c> al arrancar. Idempotente (IF NOT EXISTS /
/// CREATE OR REPLACE), por lo que se puede ejecutar en cada arranque y con múltiples réplicas.
/// core-api debe haber migrado primero (identity.tenants, uuidv7()); ver depends_on en compose.
/// </summary>
public sealed partial class IctSchemaBootstrapper(
    IServiceScopeFactory scopeFactory,
    IOptions<IctDatabaseOptions> options,
    ILogger<IctSchemaBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoMigrate)
        {
            Log.AutoMigrateDisabled(logger);
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
            foreach (var script in EmbeddedDdl.AllScriptsInOrder())
            {
                var sql = EmbeddedDdl.LoadUp(script);
                // Ejecutar con un DbCommand crudo (NO ExecuteSqlRaw): el DDL contiene llaves de
                // regex ({6,11}, {3}) que EF interpretaría como placeholders de formato ({0}, {1}).
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
                Log.ScriptApplied(logger, script);
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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "ICT schema bootstrap: DDL auto-migrate deshabilitado.")]
        public static partial void AutoMigrateDisabled(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "ICT schema bootstrap: script {Script} aplicado.")]
        public static partial void ScriptApplied(ILogger logger, string script);
    }
}
