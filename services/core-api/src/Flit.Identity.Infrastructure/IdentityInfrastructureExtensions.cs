using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Bus;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Infrastructure.Security;
using Flit.Modules.Notificaciones;
using Flit.Modules.Platform;
using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.Manifest;
using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserRoles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure;

/// <summary>
/// Epic #13217 (HU #13232) — registro de lo que comparten core-api y core-identity: login, llaves, correo, auditoría,
/// Marca Blanca, productos y Data Protection. core-api llama cada método desde el mismo punto donde antes estaban esas
/// líneas (la prueba CoreApiServiceRegistrationSnapshotTests asegura que su registro no cambió); core-identity los
/// llama sobre su propio contexto de datos.
/// </summary>
public static class IdentityInfrastructureExtensions
{
    /// <summary>
    /// Nombre de aplicación de Data Protection. <b>Tiene que ser el mismo en todos los procesos</b>: protege la cookie
    /// de la sesión del hub y las llaves de firma guardadas en <c>security.jwt_signing_keys</c>. Si difiere, un proceso
    /// no puede leer lo que cifró el otro y todos tienen que volver a iniciar sesión.
    /// </summary>
    public const string DataProtectionApplicationName = "flit-core-api";

    /// <summary>Data Protection con las llaves en la base (tabla compartida) y el nombre de aplicación común.</summary>
    public static IDataProtectionBuilder AddFlitDataProtection<TContext>(this IServiceCollection services)
        where TContext : DbContext, IDataProtectionKeyContext =>
        services.AddDataProtection()
            .PersistKeysToDbContext<TContext>()
            .SetApplicationName(DataProtectionApplicationName);

    /// <summary>
    /// Login de siempre y recuperación de cuenta: llaves y emisor del JWT, credenciales, invitaciones, activación,
    /// enlaces y canal de correo (SMTP o consola, enrutado por empresa y con bitácora de entregas).
    /// </summary>
    public static IServiceCollection AddIdentityLoginServices(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<JwtSettings>>().Value;
            return JwtKeyMaterialLoader.Load(
                settings,
                environment,
                () => PersistentJwtSigningKeyStore.LoadOrCreate(sp, settings.SigningKeyId));
        });

        services.AddScoped<IAuthUserRepository, AuthUserRepository>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IJwtTokenIssuer, RsaJwtTokenIssuer>();

        // Recuperación de contraseña (HU #10169): repos, generador de token y email.
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();

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

        // HU #13003 (A-12): URLs de los correos de seguridad por ambiente. Sin sección propia, se derivan de las que ya
        // configura cada ambiente (recursos de correo y URL pública de la marca).
        var emailSettings = configuration
            .GetSection(EmailSettings.SectionName)
            .Get<EmailSettings>() ?? new EmailSettings();
        services.AddSingleton(emailSettings);

        var emailAssets = configuration
            .GetSection(NotificationEmailAssetsOptions.SectionName)
            .Get<NotificationEmailAssetsOptions>() ?? new NotificationEmailAssetsOptions();
        services.AddSingleton(Options.Create(emailAssets));
        services.Configure<NotificationEmailAssetsOptions>(
            configuration.GetSection(NotificationEmailAssetsOptions.SectionName));
        // Bug #13194 — los correos del módulo Security (Application, sin acceso a Infrastructure)
        // leen la MISMA clave Notifications:EmailAssets:BaseUrl; sin ella, respaldo local del layout.
        services.AddSingleton(new SecurityEmailAssetsOptions
        {
            BaseUrl = configuration.GetSection(SecurityEmailAssetsOptions.SectionName)[nameof(SecurityEmailAssetsOptions.BaseUrl)]
                ?? string.Empty,
        });

        // HU #13359 (Epic #13316, el corte): core-api y core-identity no tienen transportes de correo. Todo correo, con o
        // sin empresa, se deja YA ARMADO como trabajo notificaciones.email.send en la outbox del SDK (core-api: la de
        // Trámites; core-identity: la suya) y lo envía core-notificaciones, que lleva el registro de entregas. El canal
        // lo sigue resolviendo quien arma el correo (política de la empresa).
        services.AddScoped<INotificationChannelResolver, NotificationChannelResolver>();
        services.AddScoped<IEmailSender, CorreoPorBusEmailSender>();

        return services;
    }

    /// <summary>Auditoría de configuración (quién hizo qué, en su propio scope) y contexto de auditoría de la petición.</summary>
    public static IServiceCollection AddIdentityAuditing(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditContextAccessor, HttpAuditContextAccessor>();
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        return services;
    }

    /// <summary>
    /// Marca Blanca de lectura: configuración operativa de la empresa (canal de correo), marca pública y de sesión con
    /// su caché, tema de los correos, dominios y su resolutor, y pertenencia a una red. El almacenamiento de logos lo
    /// registra cada proceso: core-api lee y escribe; core-identity solo lee.
    /// </summary>
    public static IServiceCollection AddIdentityMarcaBlanca(this IServiceCollection services, IConfiguration? configuration)
    {
        services.AddScoped<ITenantSettingsRepository, TenantSettingsRepository>();

        // HU #12412 (Feature #12366, ADR-0060 D1) — identidad de marca de la cabeza MARCA_BLANCA.
        services.AddScoped<Flit.Admin.Domain.Companies.Branding.ITenantBrandingRepository, TenantBrandingRepository>();
        // HU #12413 (Feature #12366, ADR-0060 D1) — formato/peso/dimensiones/contraste reales.
        // Mismo patrón que DomainOptions/ImprontaValidationPolicyOptions: Application consume el
        // POCO YA resuelto, sin IOptions. PermissiveBrandAssetValidator queda como respaldo
        // fail-open documentado (no se borra, solo se deja de registrar).
        if (configuration is not null)
        {
            services.Configure<Flit.Admin.Application.Companies.Branding.BrandingOptions>(
                configuration.GetSection(Flit.Admin.Application.Companies.Branding.BrandingOptions.SectionName));
            services.AddSingleton(sp =>
                sp.GetRequiredService<IOptions<Flit.Admin.Application.Companies.Branding.BrandingOptions>>().Value);
        }
        else
        {
            services.AddSingleton(new Flit.Admin.Application.Companies.Branding.BrandingOptions());
        }

        services.AddScoped<Flit.Admin.Application.Companies.Branding.IBrandAssetValidator,
            Flit.Admin.Application.Companies.Branding.BrandAssetValidator>();

        // HU #12418 (Feature #12366, ADR-0060 D2) — resolución pública/sesión de marca. Lectura
        // directa del estado del tenant (sin caché propia) + caché de 60 s del resultado resuelto,
        // sobre el MISMO IMemoryCache singleton (AddMemoryCache más abajo) que invalida
        // Publish/RetireBrandingHandler y CompanyWriteRepository.
        services.AddScoped<Flit.Admin.Application.Companies.Branding.IBrandingTenantLookup, BrandingTenantLookupRepository>();
        services.AddScoped<Flit.Admin.Application.Companies.Branding.ResolvePublicBranding.ResolvePublicBrandingHandler>();
        services.AddScoped<Flit.Admin.Application.Companies.Branding.ResolveSessionBranding.ResolveSessionBrandingHandler>();
        services.AddScoped<Flit.Admin.Application.Companies.Branding.GetPublicBrandLogo.GetPublicBrandLogoHandler>();
        services.AddSingleton<Flit.Infrastructure.Domains.MemoryPublicBrandingCache>();
        services.AddSingleton<Flit.Admin.Application.Companies.Branding.ResolvePublicBranding.IPublicBrandingCache>(
            sp => sp.GetRequiredService<Flit.Infrastructure.Domains.MemoryPublicBrandingCache>());
        services.AddSingleton<Flit.Admin.Application.Companies.Branding.IBrandingCacheInvalidator>(
            sp => sp.GetRequiredService<Flit.Infrastructure.Domains.MemoryPublicBrandingCache>());

        // HU #12428 (Feature #12405, Épica #12237 Marca Blanca, ADR-0060) — resolutor del tema de
        // correo. Misma herencia de marca que #12418, sobre el MISMO IMemoryCache singleton (arriba)
        // — MemoryPublicBrandingCache.InvalidateTenant ya limpia también esta clave.
        if (configuration is not null)
        {
            services.AddSingleton(sp =>
            {
                var options = new Flit.Infrastructure.Notifications.Theme.EmailThemePublicBrandingOptions();
                configuration.GetSection(Flit.Infrastructure.Notifications.Theme.EmailThemePublicBrandingOptions.SectionName).Bind(options);
                return options;
            });
        }
        else
        {
            services.AddSingleton(new Flit.Infrastructure.Notifications.Theme.EmailThemePublicBrandingOptions());
        }
        services.AddScoped<Flit.Modules.Security.Domain.Auth.IEmailThemeResolver,
            Flit.Infrastructure.Notifications.Theme.DbEmailThemeResolver>();

        // HU #12416 (Feature #12368, ADR-0060 D1/D2) — dominio dedicado de la red MARCA_BLANCA.
        services.AddScoped<Flit.Admin.Domain.Companies.Domains.ITenantDomainRepository, TenantDomainRepository>();
        // Resolutor por host con caché de 60 s (ADR-0060 D2): IMemoryCache es Singleton, la clase es
        // Scoped (una FlitDbContext por resolución vía ITenantDomainRepository), el caché se comparte.
        services.AddMemoryCache();
        services.AddScoped<Flit.Admin.Application.Companies.Domains.ITenantDomainResolver,
            Flit.Infrastructure.Domains.CachedTenantDomainResolver>();
        // Reservados y CNAME del borde (Domains:Reserved, Domains:EdgeTarget). Igual que
        // ImprontaValidationPolicyOptions: Application consume el POCO YA resuelto, sin IOptions.
        if (configuration is not null)
        {
            services.Configure<Flit.Admin.Application.Companies.Domains.DomainOptions>(
                configuration.GetSection(Flit.Admin.Application.Companies.Domains.DomainOptions.SectionName));
            services.AddSingleton(sp =>
                sp.GetRequiredService<IOptions<Flit.Admin.Application.Companies.Domains.DomainOptions>>().Value);
        }
        else
        {
            services.AddSingleton(new Flit.Admin.Application.Companies.Domains.DomainOptions());
        }

        // HU #12422 (Feature #12369, ADR-0060 D3) — pertenencia a una red MARCA_BLANCA (cabeza o hija)
        // + dominio activo de esa cabeza, sobre el mismo dato estructural que ITenantScopeResolver.
        // Sin caché propia: se invoca solo en login/recuperación, no en cada petición runtime.
        services.AddScoped<Flit.Modules.Security.Application.Auth.Network.ITenantNetworkMembership,
            Flit.Infrastructure.Persistence.DbTenantNetworkMembership>();

        return services;
    }

    /// <summary>
    /// Logos de marca de solo lectura para core-identity (pantalla de login y logo público), sobre el file-manager.
    /// core-api no lo usa: registra su almacenamiento completo (lee y guarda).
    /// </summary>
    public static IServiceCollection AddBrandLogoReader(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Storage.FileManagerOptions>(o => Storage.FileManagerDownloader.Configure(o, configuration));
        services.AddHttpClient<Admin.Application.Companies.Branding.IBrandLogoStorage, Storage.FileManagerBrandLogoReader>((sp, c) =>
            Storage.FileManagerDownloader.ConfigureClient(
                c, sp.GetRequiredService<IOptions<Storage.FileManagerOptions>>().Value, "los logos de marca"));
        return services;
    }

    /// <summary>Productos por empresa, acceso por producto y manifiestos (FLIT Suite, contrato §4).</summary>
    public static IServiceCollection AddPlatformStores(this IServiceCollection services)
    {
        services.AddPlatformModule();
        services.AddScoped<IProductCatalog, ProductCatalogRepository>();
        services.AddScoped<ITenantProductRepository, TenantProductRepository>();
        services.AddScoped<IProductAccessStore, ProductAccessStore>();
        services.AddScoped<IProductManifestStore, ProductManifestStore>();
        return services;
    }
}
