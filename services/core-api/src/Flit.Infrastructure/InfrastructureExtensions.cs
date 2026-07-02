using Flit.Admin.Domain.Improntas;
using Flit.Analytics.Application.Abstractions;
using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Fur;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Improntas;
using Flit.Infrastructure.Kyverum;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Infrastructure.Storage;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Modules.Security.Application;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.Modules;
using Flit.Modules.Security.Domain.Permissions;
using Flit.Modules.Security.Domain.Roles;
using Flit.Modules.Security.Domain.UserRoles;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
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
        services.AddScoped<IIdentityValidationOutboxRepository, IdentityValidationOutboxRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();

        // ── Dashboard analítico (Feature #10139, HU #10243/#10245) ───────────
        services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();
        services.AddScoped<IProcedureExcelExporter, Documents.ProcedureExcelExporter>();
        services.AddSingleton<IExecutiveSummaryPdfGenerator, Documents.ExecutiveSummaryPdfGenerator>();

        AddAttachmentStorage(services, configuration);

        // HU #10256 — FUR por overlay PdfSharpCore sobre plantillas blank.
        services.AddSingleton<IFurDocumentGenerator, FurOverlayDocumentGenerator>();
        services.AddSingleton<IExpedienteConsolidadoMerger, PdfExpedienteConsolidadoMerger>();

        AddConsultationProviders(services, configuration);
        AddIdentityValidation(services, configuration);
        AddImprontas(services, configuration);

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

        // HU #10163 — CRUD roles y asociación de permisos Super Admin
        services.AddScoped<IRoleRepository, RoleRepository>();

        // HU #10164 — Asignación única de rol por usuario tenant
        services.AddScoped<IUserRoleAssignmentRepository, UserRoleAssignmentRepository>();

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

    private static void AddAttachmentStorage(IServiceCollection services, IConfiguration configuration)
    {
        // Adjuntos en el file-manager de la empresa (S3 vía presigned URLs). Sin disco, sin
        // credenciales AWS en flit. Config primero (appsettings/`FileManager__*`), fallback a
        // env crudas FILE_MANAGER_* (mismo patrón que Verifik/Kyverum).
        string? Cfg(string key, string env) =>
            configuration[key] ?? Environment.GetEnvironmentVariable(env);

        services.Configure<FileManagerOptions>(o =>
        {
            o.BaseUrl = Cfg("FileManager:BaseUrl", "FILE_MANAGER_BASE_URL") ?? o.BaseUrl;
            o.FilesPath = Cfg("FileManager:FilesPath", "FILE_MANAGER_FILES_PATH") ?? o.FilesPath;
            o.Category = Cfg("FileManager:Category", "FILE_MANAGER_CATEGORY") ?? o.Category;
            o.TimeoutSeconds = int.TryParse(Cfg("FileManager:TimeoutSeconds", "FILE_MANAGER_TIMEOUT_SECONDS"), out var t)
                ? t : o.TimeoutSeconds;
            o.AuthToken = Cfg("FileManager:AuthToken", "FILE_MANAGER_AUTH_TOKEN") ?? o.AuthToken;
        });

        // Typed HttpClient (compatible con PublishAot, como Verifik/Kyverum). El BaseAddress apunta
        // al file-manager; las subidas/descargas a S3 usan la presigned URL absoluta (lo ignora).
        services.AddHttpClient<IAttachmentStorage, FileManagerAttachmentStorage>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<FileManagerOptions>>().Value;
            if (string.IsNullOrWhiteSpace(o.BaseUrl))
                throw new InvalidOperationException(
                    "FileManager:BaseUrl (o FILE_MANAGER_BASE_URL) es obligatoria para el almacenamiento de adjuntos.");
            var baseUrl = o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/";
            c.BaseAddress = new Uri(baseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
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

    private static void AddIdentityValidation(IServiceCollection services, IConfiguration configuration)
    {
        // HU #10233 — Kyverum Verify. Env var CRUDA primero (override de deploy 12-factor),
        // fallback a configuration (appsettings/user-secrets/`Kyverum__*`). Es OBLIGATORIO este
        // orden: appsettings.json base define valores no-nulos (Provider="mock", ApiKey="") que,
        // con la precedencia inversa, "taparían" las env vars del contenedor y nunca se leerían.
        // Un env var vacío/whitespace se trata como ausente → cae al fallback de config.
        // La API key y el secreto del webhook NUNCA se loguean.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        // Feature flag de proveedor (AC4): mock por defecto ⇒ no rompe la regresión Slice 6.
        var biometrics = new BiometricsProviderOptions
        {
            Provider = Cfg("Biometrics:Provider", "BIOMETRICS_PROVIDER") ?? Flit.Tramites.Domain.Entities.BiometricProviders.Mock,
        };
        services.AddSingleton(biometrics);

        services.Configure<KyverumOptions>(o =>
        {
            o.BaseUrl = Cfg("Kyverum:BaseUrl", "KYVERUM_BASE_URL") ?? "https://verify.kyverum.com";
            o.ApiKey = Cfg("Kyverum:ApiKey", "KYVERUM_API_KEY") ?? "";
            o.AuthScheme = Cfg("Kyverum:AuthScheme", "KYVERUM_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("Kyverum:TimeoutSeconds", "KYVERUM_TIMEOUT_SECONDS"), out var t) ? t : 30;
            o.WebhookCallbackUrl = Cfg("Kyverum:WebhookCallbackUrl", "KYVERUM_WEBHOOK_CALLBACK_URL") ?? "";
        });

        services.AddHttpClient<IKyverumVerifyClient, KyverumVerifyClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<KyverumOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Cifrado del secreto del webhook (AC2/seguridad): Data Protection API.
        // El keyring se persiste en Postgres (tabla data_protection_keys vía FlitDbContext) y se
        // fija un ApplicationName estable: así todas las réplicas comparten las mismas llaves y
        // sobreviven a reinicios. Sin esto, las llaves quedan en el filesystem efímero de cada pod
        // y el secreto HMAC del webhook de Kyverum no se puede descifrar tras un restart/otra réplica.
        services.AddDataProtection()
            .PersistKeysToDbContext<FlitDbContext>()
            .SetApplicationName("flit-core-api");
        services.AddSingleton<IWebhookSecretProtector, DataProtectionWebhookSecretProtector>();

        // Publisher de eventos (AC6): in-process por defecto; stub RabbitMQ activable por flag (fase 2).
        var messaging = Cfg("Messaging:IdentityValidation", "MESSAGING_IDENTITY_VALIDATION") ?? "inprocess";
        if (string.Equals(messaging, "rabbitmq", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IIdentityValidationEventPublisher, RabbitMqIdentityValidationEventPublisher>();
        else
            services.AddScoped<IIdentityValidationEventPublisher, InProcessIdentityValidationEventDispatcher>();

        // HU #10349 (AC4/AC6) — worker que consume los eventos 'completed' pendientes de la outbox y
        // encadena el auto-flujo (firma/FUR) de los borradores finalizados. Único para ambos modos:
        // in-process (default) y el stub RabbitMQ dejan el evento en la outbox; este servicio lo procesa.
        services.AddHostedService<IdentityValidationOutboxProcessor>();

        // Cola de ENVÍO de validaciones de identidad (provider-agnostic): proveedores registrados +
        // resolver por nombre + worker que reintenta el envío de las validaciones en 'pendiente_envio'.
        // Añadir un proveedor = registrar su IIdentityValidationProvider aquí; el worker no cambia.
        services.AddScoped<IIdentityValidationProvider, KyverumIdentityValidationProvider>();
        services.AddScoped<IIdentityValidationProviderResolver, IdentityValidationProviderResolver>();
        services.AddHostedService<IdentityValidationSendRetryProcessor>();
    }

    private static void AddImprontas(IServiceCollection services, IConfiguration configuration)
    {
        // HU #10465 — Kyverum RUNT (improntas:generar). Mismo orden de precedencia que Kyverum Verify
        // (AddIdentityValidation): env var CRUDA primero (override de deploy 12-factor), fallback a
        // configuration (appsettings/user-secrets/`ImprontaRunt__*`). runt.kyverum.com es un dominio
        // DISTINTO de verify.kyverum.com (mismo proveedor, otro producto/scope). La API key NUNCA se
        // loguea.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        services.Configure<ImprontaRuntOptions>(o =>
        {
            o.BaseUrl = Cfg("ImprontaRunt:BaseUrl", "KYVERUM_RUNT_BASE_URL") ?? "https://runt.kyverum.com";
            o.ApiKey = Cfg("ImprontaRunt:ApiKey", "KYVERUM_RUNT_API_KEY") ?? "";
            o.AuthScheme = Cfg("ImprontaRunt:AuthScheme", "KYVERUM_RUNT_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("ImprontaRunt:TimeoutSeconds", "KYVERUM_RUNT_TIMEOUT_SECONDS"), out var t)
                ? t : 30;
        });

        services.AddHttpClient<IImprontaExternalClient, ImprontaRuntClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<ImprontaRuntOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
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
