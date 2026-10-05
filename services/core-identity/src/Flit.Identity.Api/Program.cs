using Flit.Api.Authorization;
using Flit.Api.Identity;
using Flit.Api.Middleware;
using Flit.Api.Platform;
using Flit.Api.RateLimiting;
using Flit.Identity.Web;
using Flit.Infrastructure;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Application;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.Network;

namespace Flit.Identity.Api;

/// <summary>
/// core-identity (Epic #13217, HU #13233). Solo lo que el login necesita, sobre su propio contexto de datos: no carga
/// Trámites, OT, reportes ni consultas, no corre procesos de negocio ni migraciones. El orden de los middlewares es el
/// mismo de core-api (dominio sellado → autenticación → atadura del token al dominio → autorización).
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = Build(args);
        await app.RunAsync().ConfigureAwait(false);
    }

    internal static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        UseSharedBaseSettings(builder);
        configure?.Invoke(builder);

        // Igual que core-api: el contenedor se valida al arrancar, no en la primera petición.
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        var connectionString = builder.Configuration.GetConnectionString("Core")
            ?? throw new InvalidOperationException("ConnectionStrings:Core (PostgreSQL) es obligatoria.");
        AddIdentityServices(builder.Services, builder.Configuration, builder.Environment, connectionString);

        var app = builder.Build();
        UseIdentityPipeline(app);
        return app;
    }

    /// <summary>
    /// La configuración base es el <c>appsettings.json</c> de core-api, enlazado: se copia junto al binario pero no a la
    /// carpeta del proyecto. En el contenedor la raíz de contenido es esa carpeta del binario; en pruebas y al correrlo
    /// desde el proyecto no lo es, y sin esto core-identity arrancaría sin dominios, hosts, clientes OIDC ni correo. Se
    /// conserva el orden de precedencia (los archivos siguen debajo de variables de ambiente y argumentos).
    /// </summary>
    private static void UseSharedBaseSettings(WebApplicationBuilder builder)
    {
        var sources = builder.Configuration.Sources;
        var baseDirectory = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource { Path: { } path } json
                && path.StartsWith("appsettings", StringComparison.Ordinal)
                && !File.Exists(Path.Combine(builder.Environment.ContentRootPath, path))
                && baseDirectory.GetFileInfo(path).Exists)
            {
                sources[i] = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
                {
                    Path = path,
                    Optional = json.Optional,
                    ReloadOnChange = false,
                    FileProvider = baseDirectory,
                };
            }
        }
    }

    internal static void AddIdentityServices(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, string connectionString)
    {
        // Datos: el contexto de identidad (sin migraciones) y el anillo de Data Protection compartido con core-api.
        services.AddDbContext<IdentityDbContext>(options => NpgsqlConventions.Apply(options, connectionString));
        services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddFlitDataProtection<IdentityDbContext>();

        // Infraestructura compartida: login, llaves, correo (con el canal Renting), auditoría, Marca Blanca y productos.
        services.AddRentingChannel(configuration);
        services.AddIdentityLoginServices(configuration, environment);
        services.AddIdentityAuditing();
        services.AddIdentityMarcaBlanca(configuration);
        services.AddBrandLogoReader(configuration);
        services.AddPlatformStores();
        services.AddPlatformApi(configuration);

        // Casos de uso del login y la recuperación de cuenta.
        services.AddScoped<INetworkUrlBaseResolver, NetworkUrlBaseResolver>();
        services.AddIdentityAuthApplication();

        // Tokens: los mismos que valida core-api, y el servidor OIDC del hub.
        services.AddFlitTokenValidation(configuration, environment);
        services.AddFlitSessionExpiredResponses();
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminAuthorization.SuperAdminPolicy, policy => policy.RequireSuperAdmin());
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, PlatformForbiddenResultHandler>();
        services.AddFlitOidcAcceptance<IdentityDbContext>(configuration);
        services.AddFlitOidcServer(configuration);

        // Dominio sellado por el gateway (Marca Blanca) y límite de tasa de la marca pública.
        services.AddHttpContextAccessor();
        services.AddScoped<IDomainContextAccessor, HttpDomainContextAccessor>();
        services.Configure<PublicBrandingOptions>(configuration.GetSection(PublicBrandingOptions.SectionName));
        services.AddPublicBrandingRateLimiter();

        services.AddSingleton<IdentitySchemaReadiness>();
    }

    internal static void UseIdentityPipeline(WebApplication app)
    {
        app.UseRateLimiter();
        app.UseMiddleware<DomainContextMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<DomainBindingMiddleware>();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/api/v1/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (IdentitySchemaReadiness readiness, IdentityDbContext db, CancellationToken ct) =>
            await readiness.CheckAsync(db, ct).ConfigureAwait(false) is { } problem
                ? Results.Json(new { status = problem }, statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(new { status = "ready" })).AllowAnonymous();

        app.MapFlitIdentityEndpoints();
    }
}

/// <summary>Tipo de referencia del ensamblado para <c>WebApplicationFactory</c> en las pruebas (Program es estático).</summary>
public sealed class IdentityApiEntryPoint;
