using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Cómo se conectan a PostgreSQL los contextos de FLIT (HU #13231): reintentos, snake_case y sin datos sensibles en el
/// log. Lo usan <c>FlitDbContext</c> (core-api) e <see cref="IdentityDbContext"/> (core-identity), así no pueden quedar
/// con convenciones distintas sobre las mismas tablas.
/// </summary>
public static class NpgsqlConventions
{
    public static DbContextOptionsBuilder Apply(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention()
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false)
            // HU10175: UserInvitation se crea con SQL crudo en HU10147_Invitations; el snapshot no la registra, así que EF
            // lanza PendingModelChangesWarning. Se ignora porque la tabla existe y la migración ya fue aplicada vía SQL.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
}
