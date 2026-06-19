using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Modules.Security.Application;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flit.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddPostgresInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<FlitDbContext>((serviceProvider, opts) =>
            opts.UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                })
            .UseSnakeCaseNamingConvention()
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false));

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtSettings>>().Value;
            return JwtKeyMaterialLoader.Load(settings, environment);
        });

        services.AddScoped<IAuthUserRepository, AuthUserRepository>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IJwtTokenIssuer, RsaJwtTokenIssuer>();
        services.AddSecurityApplication();

        return services;
    }

    public static async Task InitializeInfrastructureAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Database.MigrateAsync(cancellationToken);
        await DevelopmentAuthSeeder.SeedAsync(db, hasher, env, cancellationToken);
    }
}
