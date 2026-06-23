using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Infrastructure.Storage;
using Flit.Modules.Security.Application;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.Modules;
using Flit.Modules.Security.Domain.Permissions;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
            .EnableDetailedErrors(false)
            // HU10175: UserInvitation se crea con SQL crudo en HU10147_Invitations; el snapshot
            // no la registra, así que EF lanza PendingModelChangesWarning. Se ignora porque
            // la tabla existe y la migración ya fue aplicada vía SQL.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

        // ── Runtime de trámites (rework #10128) ──────────────────────────────
        services.AddScoped<IProcedureTypeRepository, ProcedureTypeRepository>();
        services.AddScoped<IProcedureInstanceRepository, ProcedureInstanceRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();

        AddAttachmentStorage(services, configuration, environment);
        AddConsultationProviders(services, configuration);

        // ── Seguridad / login (HU #10168, #10169) ────────────────────────────
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<JwtSettings>>().Value;
            return JwtKeyMaterialLoader.Load(settings, environment);
        });

        services.AddScoped<IAuthUserRepository, AuthUserRepository>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IJwtTokenIssuer, RsaJwtTokenIssuer>();

        // Recuperación de contraseña (HU #10169): repos, generador de token y email.
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();

        // HU #10161 — CRUD módulos dinámicos Super Admin
        services.AddScoped<ISecurityModuleRepository, SecurityModuleRepository>();

        // HU #10162 — CRUD permisos granulares Super Admin
        services.AddScoped<IPermissionRepository, PermissionRepository>();

        // Invitaciones (HU #10175) y activación de cuenta (HU #10177).
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IUserActivationRepository, UserActivationRepository>();
        var invitationOptions = configuration
            .GetSection(InvitationOptions.SectionName)
            .Get<InvitationOptions>() ?? new InvitationOptions();
        services.AddSingleton(invitationOptions);
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();

        var passwordRecovery = configuration
            .GetSection(PasswordRecoveryOptions.SectionName)
            .Get<PasswordRecoveryOptions>() ?? new PasswordRecoveryOptions();
        services.AddSingleton(passwordRecovery);

        var emailSettings = configuration
            .GetSection(EmailSettings.SectionName)
            .Get<EmailSettings>() ?? new EmailSettings();
        services.AddSingleton(emailSettings);

        // SMTP real, o consola en Development cuando no hay host configurado.
        if (environment.IsDevelopment() && string.IsNullOrWhiteSpace(emailSettings.Host))
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        else
            services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddSecurityApplication();

        return services;
    }

    private static void AddAttachmentStorage(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Raíz configurable: ATTACHMENTS_ROOT (env) > sección Attachments:Root (config) >
        // default {ContentRoot}/uploads/tramites. Relativa ⇒ se ancla al content root.
        var configured = Environment.GetEnvironmentVariable("ATTACHMENTS_ROOT")
            ?? configuration[$"{AttachmentStorageOptions.SectionName}:Root"];

        string root;
        if (string.IsNullOrWhiteSpace(configured))
            root = Path.Combine(environment.ContentRootPath, "uploads", "tramites");
        else if (Path.IsPathRooted(configured))
            root = configured;
        else
            root = Path.Combine(environment.ContentRootPath, configured);

        services.AddSingleton<IAttachmentStorage>(_ => new DiskAttachmentStorage(root));
    }

    private static void AddConsultationProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Convención del repo: config primero (appsettings.json en local; claves
        // `Verifik__`/`Consultations__` en el .env de docker vía IConfiguration), con
        // fallback a las env vars crudas VERIFIK_*/INTEMPO_* (compat con .env.verifik).
        string? Cfg(string key, string env) =>
            configuration[key] ?? Environment.GetEnvironmentVariable(env);

        // Modos real|mock por proveedor.
        services.Configure<ConsultationProviderModeOptions>(o =>
        {
            o.VerifikVehicleMode = Cfg("Consultations:VerifikVehicleMode", "VERIFIK_VEHICLE_MODE") ?? "real";
            o.VerifikSimitMode = Cfg("Consultations:VerifikSimitMode", "VERIFIK_SIMIT_MODE") ?? "mock";
            o.VerifikRnmcMode = Cfg("Consultations:VerifikRnmcMode", "VERIFIK_RNMC_MODE") ?? "mock";
            o.VerifikConductorMode = Cfg("Consultations:VerifikConductorMode", "VERIFIK_CONDUCTOR_MODE") ?? "mock";
            o.IntempoMode = Cfg("Consultations:IntempoMode", "INTEMPO_MODE") ?? "mock";
        });

        // Config Verifik. Clave de config `Verifik:BearerToken` (alineada con el
        // docker-compose), fallback a la env cruda VERIFIK_API_TOKEN.
        services.Configure<VerifikOptions>(o =>
        {
            o.BaseUrl = Cfg("Verifik:BaseUrl", "VERIFIK_BASE_URL") ?? "https://api.verifik.co";
            o.ApiToken = Cfg("Verifik:BearerToken", "VERIFIK_API_TOKEN") ?? "";
            o.AuthScheme = Cfg("Verifik:AuthScheme", "VERIFIK_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("Verifik:TimeoutSeconds", "VERIFIK_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        // Config INTEMPO.
        services.Configure<IntempoOptions>(o =>
        {
            o.BaseUrl = Cfg("Intempo:BaseUrl", "INTEMPO_BASE_URL") ?? "https://www.moviliza.com.co";
            o.TimeoutSeconds = int.TryParse(Cfg("Intempo:TimeoutSeconds", "INTEMPO_TIMEOUT_SECONDS"), out var t) ? t : 15;
        });

        // Typed HttpClients (compatibles con PublishAot).
        services.AddHttpClient<VerifikConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikSimitConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikRnmcConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikConductorConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<IntempoConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<IntempoOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Proveedores expuestos como IConsultationProvider para el registry.
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikSimitConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikRnmcConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConductorConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<IntempoConsultationProvider>());
        services.AddSingleton<IConsultationProvider, FlitIntegrationsGatewayProvider>();
        services.AddScoped<IConsultationProviderRegistry, ConsultationProviderRegistry>();
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
