using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.DeliveryLog;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Infrastructure.Security;
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
        var emailLinks = configuration.GetSection(EmailLinksOptions.SectionName).Get<EmailLinksOptions>() ?? new EmailLinksOptions();
        if (string.IsNullOrWhiteSpace(emailLinks.AssetsBaseUrl))
            emailLinks.AssetsBaseUrl = configuration["Notifications:EmailAssets:BaseUrl"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(emailLinks.LoginUrl) && configuration["PublicBranding:PublicBaseUrl"] is { Length: > 0 } publicBase)
            emailLinks.LoginUrl = publicBase.TrimEnd('/') + "/login";
        services.AddSingleton(emailLinks);

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

        // SMTP real, o consola cuando no hay host configurado y Smtp:UseConsoleWhenNoHost está encendida (HU #12895,
        // A-02: por defecto, solo en Development).
        // HU #11358 AC5 — Scoped (no Singleton): todos los AddHttpClient<T> del repo son
        // Transient, así que un adaptador HTTP debajo de IEmailSender (HU #11361) sería una
        // dependencia cautiva si el puerto siguiera siendo instancia única.
        var useConsoleEmailSender = configuration.GetValue("Smtp:UseConsoleWhenNoHost", environment.IsDevelopment())
            && string.IsNullOrWhiteSpace(emailSettings.Host);
        if (useConsoleEmailSender)
            services.AddScoped<ConsoleEmailSender>();
        else
            services.AddScoped<SmtpEmailSender>();

        // HU #11368 (Feature #11349, AC8) — mismo booleano que decide el transporte, publicado como
        // Singleton para que el banco de pruebas de notificaciones pueda declarar "esto fue consola,
        // no salió correo real" sin inspeccionar el árbol de DI (ver EmailTransportDescriptor).
        services.AddSingleton(new EmailTransportDescriptor(useConsoleEmailSender));

        // HU #11363 (Feature #11348) — decorador que envuelve el sender real y escribe la bitácora
        // append-only admin.notification_delivery_logs SIN tocar los 6 puntos de llamada de
        // IEmailSender: mide duración con Stopwatch, delega el envío y registra el intento en un
        // scope PROPIO (aislado del DbContext ambiente de la petición). Un fallo al escribir la
        // bitácora NUNCA cambia el resultado del envío (AC6) — ver NotificationDeliveryLoggingEmailSender.
        services.AddScoped<INotificationDeliveryLogWriter, NotificationDeliveryLogWriter>();

        // HU #11371 (Feature #11349, cierra el retorno-temprano fijo del banco de pruebas) —
        // TenantChannelEmailRouter deja de construirse INLINE dentro de la fábrica de IEmailSender:
        // se registra como servicio propio (Scoped) para que el banco de pruebas de notificaciones
        // pueda alcanzarlo vía IExplicitChannelEmailSender y enviar por un canal explícito. El orden
        // del pipeline de producción NO cambia: la fábrica de IEmailSender de abajo sigue resolviendo
        // ESTA MISMA instancia como el "concreteSender" que NotificationDeliveryLoggingEmailSender
        // envuelve — los 6 puntos de llamada de producción siguen viendo el mismo decorador
        // envolviendo al mismo router. IRentingEmailApiSender solo está registrado cuando
        // AddRentingChannel lo habilitó (RENTING_API_ENABLED=true); sp.GetService (no
        // GetRequiredService) lo resuelve como null en cualquier otro ambiente — el router trata ese
        // null como "canal no disponible" y responde ConfigurationIncomplete en vez de fallar al
        // resolver el árbol de DI.
        services.AddScoped<INotificationChannelResolver, NotificationChannelResolver>();

        services.AddScoped(sp =>
        {
            IEmailSender flitTransport = useConsoleEmailSender
                ? sp.GetRequiredService<ConsoleEmailSender>()
                : sp.GetRequiredService<SmtpEmailSender>();

            return new TenantChannelEmailRouter(
                flitTransport,
                sp.GetRequiredService<INotificationChannelResolver>(),
                sp.GetService<IRentingEmailApiSender>(),
                sp.GetRequiredService<IOptions<RentingChannelOptions>>(),
                sp.GetRequiredService<ILogger<TenantChannelEmailRouter>>());
        });
        services.AddScoped<IExplicitChannelEmailSender>(sp => sp.GetRequiredService<TenantChannelEmailRouter>());

        services.AddScoped<IEmailSender>(sp =>
        {
            TenantChannelEmailRouter router = sp.GetRequiredService<TenantChannelEmailRouter>();

            return new NotificationDeliveryLoggingEmailSender(
                router,
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<NotificationDeliveryLoggingEmailSender>>(),
                sp.GetRequiredService<EmailSettings>());
        });

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

    /// <summary>
    /// HU #11359 — canal de API del cliente Renting (envío de correo por API externa con mTLS).
    /// OPT-IN por <see cref="RentingChannelOptions.Enabled"/> (AC2): sin el interruptor en
    /// <c>true</c> se registra únicamente <c>IOptions&lt;RentingChannelOptions&gt;</c> con
    /// <c>Enabled = false</c> — para que la HU #11362 (enrutamiento, AC6) pueda consultarlo sin
    /// saber de antemano si el canal está habilitado — y NADA MÁS se valida ni se registra: ni el
    /// material TLS, ni el <see cref="System.Net.Http.HttpClient"/>.
    /// <para>
    /// Habilitado, valida la presencia de TODAS las variables obligatorias (AC1) y registra un
    /// <see cref="RentingClientCertificateProvider"/> <c>Singleton</c> cuyo <c>factory</c> carga y
    /// valida el certificado (<see cref="RentingClientCertificateLoader"/>). Con
    /// <c>ValidateOnBuild</c> —patrón vigente del repo, ver
    /// <see cref="Security.JwtKeyMaterialLoader"/>— esa carga corre AL CONSTRUIR el
    /// <see cref="IServiceProvider"/>, o sea en el arranque: un certificado inexistente, una
    /// passphrase que no abre el archivo o una identidad de login que no coincide con el Subject
    /// del certificado (AC4/AC5) tumban el arranque completo, no solo el primer envío.
    /// </para>
    /// </summary>
    public static void AddRentingChannel(this IServiceCollection services, IConfiguration configuration)
    {
        // Env var CRUDA primero (a diferencia de Fasecolda, que va config primero): este es un
        // canal 100% de despliegue (12-factor), sin defaults propios de negocio en appsettings.json
        // que deban ganarle a la variable del contenedor — mismo orden y motivo que Rues/Kyverum.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        var enabled = string.Equals(
            Cfg("Notifications:Renting:Enabled", "RENTING_API_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

        if (!enabled)
        {
            // AC2 — el canal nace deshabilitado: no se exige ninguna variable del canal ni el
            // material TLS. El servicio arranca con normalidad.
            services.Configure<RentingChannelOptions>(o => o.Enabled = false);
            return;
        }

        static string Require(string? value, string envName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Canal Renting habilitado (RENTING_API_ENABLED=true) pero falta la variable de "
                    + $"configuración '{envName}'.");
            }

            return value;
        }

        static int RequireInt(string? value, string envName)
        {
            if (!int.TryParse(value, out var result))
            {
                throw new InvalidOperationException(
                    $"Canal Renting habilitado (RENTING_API_ENABLED=true) pero la variable de "
                    + $"configuración '{envName}' es obligatoria y debe ser un entero (segundos).");
            }

            return result;
        }

        // ADR-0044 — interruptor AFIRMATIVO y PROPIO del despliegue: RENTING_API_ENABLED=true no
        // vuelve a consultar IHostEnvironment para decidir si desvía. Tri-estado, a propósito:
        // AUSENTE/VACÍA distingue del valor ININTELIGIBLE (bool.TryParse a secas no basta, porque
        // "" y "ture" tratados igual dejarían degradar en silencio un error de escritura).
        //   - ausente o vacía  ⇒ desviar (default seguro)
        //   - "false"          ⇒ desviar (declaración explícita del default)
        //   - "true"           ⇒ enviar real (en CUALQUIER ambiente)
        //   - cualquier otro valor no vacío ⇒ falla el arranque (no degrada en silencio)
        var realRecipientsRaw = Cfg(
            "Notifications:Renting:SendEmailRealRecipientsEnabled",
            "RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED");

        // Variable derogada (HU #11364 original). Su sola PRESENCIA con valor no vacío, con el
        // canal encendido, tumba el arranque — sin importar el valor de la variable nueva — para
        // que un despliegue viejo no crea que sigue gobernando el desvío.
        var deprecatedOverrideRaw = Cfg(
            "Notifications:Renting:SendEmailDevelopmentRecipientOverrideEnabled",
            "RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED");
        if (!string.IsNullOrWhiteSpace(deprecatedOverrideRaw))
        {
            throw new InvalidOperationException(
                "Canal Renting: la variable 'RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED' "
                + "quedó DEROGADA (ADR-0044) y ya no gobierna el desvío de destinatario. Retírela del "
                + "despliegue. La decisión ahora la toma 'RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED' "
                + "— el valor seguro es NO declararla (equivale a desviar al buzón de control).");
        }

        bool sendRealRecipients;
        if (string.IsNullOrWhiteSpace(realRecipientsRaw))
        {
            sendRealRecipients = false;
        }
        else if (string.Equals(realRecipientsRaw, "true", StringComparison.OrdinalIgnoreCase))
        {
            sendRealRecipients = true;
        }
        else if (string.Equals(realRecipientsRaw, "false", StringComparison.OrdinalIgnoreCase))
        {
            sendRealRecipients = false;
        }
        else
        {
            throw new InvalidOperationException(
                "Canal Renting habilitado (RENTING_API_ENABLED=true) pero la variable de configuración "
                + "'RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED' tiene un valor no reconocido. Valores "
                + "válidos: 'true', 'false', o ausente/vacía (equivalente a 'false' — desvía al buzón de "
                + "control).");
        }

        var divertRecipients = !sendRealRecipients;

        // AC1 — se valida la presencia de TODAS las variables requeridas: ruta del certificado,
        // passphrase, URL base, ruta de envío, ruta de login, tiempos de espera y datos del
        // remitente. Las variables de caché de login (uso de la HU #11360) y las del "otro proxy"
        // que comparte bloque de configuración (DEFAULT_SENDER_*) se modelan pero NO se exigen
        // aquí — quedan fuera del alcance de esta HU (ver comentarios en RentingChannelOptions). El
        // destinatario de desvío SÍ se exige cuando el interruptor está activo: sin él, el
        // interruptor encendido no tendría a dónde desviar y el envío fallaría en silencio en vez
        // de proteger al cliente final.
        var options = new RentingChannelOptions
        {
            Enabled = true,
            BaseUrl = Require(Cfg("Notifications:Renting:BaseUrl", "RENTING_API_BASE_URL"), "RENTING_API_BASE_URL"),
            ApiKeyName = Require(
                Cfg("Notifications:Renting:ApiKeyName", "RENTING_API_KEY_NAME"), "RENTING_API_KEY_NAME"),
            ApiKeyValue = Require(
                Cfg("Notifications:Renting:ApiKeyValue", "RENTING_API_KEY_VALUE"), "RENTING_API_KEY_VALUE"),
            PfxCertificatePath = Require(
                Cfg("Notifications:Renting:PfxCertificatePath", "RENTING_API_PFX_CERTIFICATE_PATH"),
                "RENTING_API_PFX_CERTIFICATE_PATH"),
            Passphrase = Require(
                Cfg("Notifications:Renting:Passphrase", "RENTING_API_PASSPHRASE"), "RENTING_API_PASSPHRASE"),
            SecondsTimeout = RequireInt(
                Cfg("Notifications:Renting:SecondsTimeout", "RENTING_API_SECONDS_TIMEOUT"),
                "RENTING_API_SECONDS_TIMEOUT"),
            LoginPath = Require(
                Cfg("Notifications:Renting:LoginPath", "RENTING_API_LOGIN_PATH"), "RENTING_API_LOGIN_PATH"),
            LoginSecondsTimeout = RequireInt(
                Cfg("Notifications:Renting:LoginSecondsTimeout", "RENTING_API_LOGIN_SECONDS_TIMEOUT"),
                "RENTING_API_LOGIN_SECONDS_TIMEOUT"),
            LoginSubject = Require(
                Cfg("Notifications:Renting:LoginSubject", "RENTING_API_LOGIN_SUBJECT"), "RENTING_API_LOGIN_SUBJECT"),
            SendEmailPath = Require(
                Cfg("Notifications:Renting:SendEmailPath", "RENTING_API_SEND_EMAIL_PATH"),
                "RENTING_API_SEND_EMAIL_PATH"),
            SendEmailSecondsTimeout = RequireInt(
                Cfg("Notifications:Renting:SendEmailSecondsTimeout", "RENTING_API_SEND_EMAIL_SECONDS_TIMEOUT"),
                "RENTING_API_SEND_EMAIL_SECONDS_TIMEOUT"),
            SendEmailSenderEmail = Require(
                Cfg("Notifications:Renting:SendEmailSenderEmail", "RENTING_API_SEND_EMAIL_SENDER_EMAIL"),
                "RENTING_API_SEND_EMAIL_SENDER_EMAIL"),
            SendEmailSenderUsername = Require(
                Cfg("Notifications:Renting:SendEmailSenderUsername", "RENTING_API_SEND_EMAIL_SENDER_USERNAME"),
                "RENTING_API_SEND_EMAIL_SENDER_USERNAME"),

            // No exigidas por esta HU (ver comentario arriba): se modelan si están presentes.
            LoginCacheKey = Cfg("Notifications:Renting:LoginCacheKey", "RENTING_API_LOGIN_CACHE_KEY") ?? "",
            LoginCacheSecondsTtl = int.TryParse(
                Cfg("Notifications:Renting:LoginCacheSecondsTtl", "RENTING_API_LOGIN_CACHE_SECONDS_TTL"),
                out var cacheTtl)
                ? cacheTtl
                : 3600,
            LoginSecretName = Cfg("Notifications:Renting:LoginSecretName", "RENTING_API_LOGIN_SECRET_NAME") ?? "",
            DefaultSenderEmail = Cfg(
                "Notifications:Renting:DefaultSenderEmail", "RENTING_API_SEND_EMAIL_DEFAULT_SENDER_EMAIL") ?? "",
            DefaultSenderUsername = Cfg(
                "Notifications:Renting:DefaultSenderUsername", "RENTING_API_SEND_EMAIL_DEFAULT_SENDER_USERNAME") ?? "",
            // ADR-0044 — con el desvío activo (default seguro) el buzón de control es OBLIGATORIO:
            // sin él el interruptor no tendría a dónde desviar. Con envío real, el buzón puede
            // quedar vacío (no hay desvío que necesite dónde caer).
            SendEmailDevelopmentRecipientEmail = divertRecipients
                ? Require(
                    Cfg(
                        "Notifications:Renting:SendEmailDevelopmentRecipientEmail",
                        "RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_EMAIL"),
                    "RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_EMAIL")
                : Cfg(
                    "Notifications:Renting:SendEmailDevelopmentRecipientEmail",
                    "RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_EMAIL") ?? "",
            SendEmailDevelopmentRecipientUsername = divertRecipients
                ? Require(
                    Cfg(
                        "Notifications:Renting:SendEmailDevelopmentRecipientUsername",
                        "RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_USERNAME"),
                    "RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_USERNAME")
                : Cfg(
                    "Notifications:Renting:SendEmailDevelopmentRecipientUsername",
                    "RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_USERNAME") ?? "",
            SendRealRecipientsEnabled = sendRealRecipients,
        };

        // ADR-0044 — ninguna rama de este método vuelve a consultar IHostEnvironment: la decisión
        // de desviar/enviar real ya quedó resuelta arriba, únicamente por
        // RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED.

        services.Configure<RentingChannelOptions>(o =>
        {
            o.Enabled = options.Enabled;
            o.BaseUrl = options.BaseUrl;
            o.ApiKeyName = options.ApiKeyName;
            o.ApiKeyValue = options.ApiKeyValue;
            o.PfxCertificatePath = options.PfxCertificatePath;
            o.Passphrase = options.Passphrase;
            o.SecondsTimeout = options.SecondsTimeout;
            o.LoginPath = options.LoginPath;
            o.LoginSecondsTimeout = options.LoginSecondsTimeout;
            o.LoginCacheKey = options.LoginCacheKey;
            o.LoginCacheSecondsTtl = options.LoginCacheSecondsTtl;
            o.LoginSecretName = options.LoginSecretName;
            o.LoginSubject = options.LoginSubject;
            o.SendEmailPath = options.SendEmailPath;
            o.SendEmailSecondsTimeout = options.SendEmailSecondsTimeout;
            o.SendEmailSenderEmail = options.SendEmailSenderEmail;
            o.SendEmailSenderUsername = options.SendEmailSenderUsername;
            o.DefaultSenderEmail = options.DefaultSenderEmail;
            o.DefaultSenderUsername = options.DefaultSenderUsername;
            o.SendEmailDevelopmentRecipientEmail = options.SendEmailDevelopmentRecipientEmail;
            o.SendEmailDevelopmentRecipientUsername = options.SendEmailDevelopmentRecipientUsername;
            o.SendRealRecipientsEnabled = options.SendRealRecipientsEnabled;
        });

        // AC3/AC4/AC5 (HU #11359/#11360) — Singleton cuyo factory carga y valida el certificado.
        // Con ValidateOnBuild esto se ejecuta al construir el IServiceProvider (arranque), no en el
        // primer uso. ADR-0044 — mismo checkpoint de arranque: se aprovecha para dejar en el log
        // real (no un logger de arranque aparte) en qué modo queda el canal — sin registrar
        // secretos ni direcciones de correo.
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Flit.Infrastructure.Notifications.Renting");
            RentingChannelStartupLog.LogMode(logger, divertRecipients);
            var certificate = RentingClientCertificateLoader.Load(options, logger);
            return new RentingClientCertificateProvider(certificate);
        });

        // Cliente HTTP de transporte con el certificado cliente adjunto (mTLS) y verificación del
        // servidor ACTIVA (no se toca la validación por defecto). Solo transporte: el adaptador de
        // envío/multipart es de la HU #11361 y el login es de la HU #11360.
        services.AddHttpClient(RentingChannelOptions.HttpClientName, (sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<RentingChannelOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.SecondsTimeout);
            if (!string.IsNullOrWhiteSpace(o.ApiKeyName))
                c.DefaultRequestHeaders.TryAddWithoutValidation(o.ApiKeyName, o.ApiKeyValue);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var certificateProvider = sp.GetRequiredService<RentingClientCertificateProvider>();
            return RentingHttpMessageHandlerFactory.Create(certificateProvider.Certificate);
        });

        // HU #11360 — login, caché de token (anti-estampida, AC1/AC2/AC3) y el ejecutor que aplica
        // la política de reintento ante 401 (AC4/AC5/AC6). El reloj es TimeProvider inyectado (no
        // DateTimeOffset.UtcNow directo) para que las pruebas de TTL puedan adelantar el tiempo sin
        // dormir el TTL real; TryAddSingleton porque otro punto de composición puede haberlo
        // registrado ya. IRentingTokenCache DEBE ser Singleton: su estado (el token cacheado y el
        // semáforo de anti-estampida) tiene que sobrevivir entre requests — si fuera Scoped, cada
        // request vería la caché vacía y el AC1/AC2 dejarían de cumplirse. Que dependa de
        // IRentingLoginClient (Transient) no es dependencia cautiva: .NET solo prohíbe que un
        // Singleton dependa de un Scoped, no de un Transient.
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<IRentingLoginClient, RentingLoginClient>();
        services.AddSingleton<IRentingTokenCache, RentingTokenCache>();
        services.AddScoped<RentingAuthenticatedRequestExecutor>();

        // HU #11361 — adaptador de envío/multipart. HU #11364 — IRentingRecipientOverride es el
        // desvío OBLIGATORIO de destinatario fuera de producción: se registra SIEMPRE que el canal
        // esté habilitado (única rama en la que este método corre) porque la propia implementación
        // decide, por su interruptor propio (AC5), si desvía o no — nunca por el ambiente. La
        // validación de arranque de arriba (AC3/AC4) ya garantiza que el interruptor está en el
        // valor correcto para el ambiente actual. Quién CONSUME IRentingEmailApiSender es la
        // HU #11362 (enrutamiento) — no se enchufa a IEmailSender aquí.
        services.TryAddSingleton<IRentingRecipientOverride, RentingRecipientOverride>();
        services.AddScoped<IRentingEmailApiSender, RentingEmailApiSender>();
    }
}

/// <summary>
/// ADR-0044 — log de arranque (source-generated, CA1848) que deja constancia inequívoca del modo
/// en que quedó el canal Renting. Nunca registra secretos ni direcciones de correo: solo el modo.
/// </summary>
internal static partial class RentingChannelStartupLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Canal Renting: arranca en modo DESVÍO. Todo envío por este canal va al buzón de "
            + "control, no a destinatarios reales (RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED "
            + "ausente/vacía o 'false').")]
    public static partial void LogDivertMode(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Canal Renting: arranca en modo ENVÍO REAL. ESTE DESPLIEGUE ENVÍA A DESTINATARIOS "
            + "REALES DE CLIENTES por la API PRODUCTIVA de Renting "
            + "(RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED=true).")]
    public static partial void LogRealRecipientsMode(ILogger logger);

    public static void LogMode(ILogger logger, bool divertRecipients)
    {
        if (divertRecipients)
            LogDivertMode(logger);
        else
            LogRealRecipientsMode(logger);
    }
}
