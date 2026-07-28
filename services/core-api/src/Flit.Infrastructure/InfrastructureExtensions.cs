using Flit.Analytics.Application.Abstractions;
using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Consultations.Avaluos;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Fur;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Ict;
using Flit.Infrastructure.Improntas;
using Flit.Infrastructure.KyverumRunt;
using Flit.Infrastructure.Rues;
using Flit.Infrastructure.Kyverum;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Ocr;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Infrastructure.Storage;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Ocr;
using Flit.Modules.Improntas.Domain;
using Flit.Modules.Security.Application;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.Modules;
using Flit.Modules.Security.Domain.Permissions;
using Flit.Modules.Security.Domain.Roles;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;
using Flit.Infrastructure.Quipux;
using Flit.Modules.Quipux.Application;
using Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Consola;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.LogQx;
using Flit.Modules.Quipux.Domain.Puertos;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
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
        // FEATURE-08 / HU-BE-01 (CFD-01/AC#5) — snapshot inmutable del tipo por instancia.
        services.AddScoped<IProcedureTypeSnapshotRepository, ProcedureTypeSnapshotRepository>();
        // FEATURE-08 / HU-BE-03 (CFD-04) — fuentes externas por tipo (catálogo global).
        services.AddScoped<IProcedureTypeSourceRepository, ProcedureTypeSourceRepository>();
        // FEATURE-08 / HU-BE-04 (CFD-06) — requisitos documentales por tipo (configurador dinámico).
        services.AddScoped<IProcedureTypeDocumentRepository, ProcedureTypeDocumentRepository>();
        // FEATURE-08 / HU-BE-06 (CFD-09) — feature flag F08_DynamicProcedures (por tenant, ot_feature_flags).
        services.AddScoped<Flit.Tramites.Application.UseCases.ProcedureInstances.IDynamicProceduresPolicy,
            OtRules.DynamicProceduresPolicy>();
        services.AddScoped<IProcedureInstanceRepository, ProcedureInstanceRepository>();
        // IT-3 (Feature #10585) — persistencia del agregado de prenda.
        services.AddScoped<IProcedureInstancePrendaRepository, ProcedureInstancePrendaRepository>();
        services.AddScoped<IIdentityValidationOutboxRepository, IdentityValidationOutboxRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        // HU #10878 (Feature #10862, CF-04) — caché cross-trámite de consultas externas (ADR-0030)
        // + gate de consentimiento Habeas Data para el reúso de datos de persona (ADR-0031).
        services.AddScoped<Flit.Tramites.Domain.Repositories.IExternalQueryCacheRepository, ExternalQueryCacheRepository>();
        services.AddScoped<Flit.Tramites.Domain.Repositories.IPersonDataConsentRepository, PersonDataConsentRepository>();
        // HU #10865 — entidad persona/sujeto a nivel tenant (Feature #10864, CF-00, ADR-0030).
        services.AddScoped<Flit.Tramites.Domain.Repositories.IPersonRepository, PersonRepository>();
        // HU #10520 — catálogo de tipos de documento para validación de carga por tipo (MIME/tamaño).
        services.AddScoped<Flit.Tramites.Domain.Tramites.Catalog.IDocumentTypeCatalog, DocumentTypeCatalog>();
        // HU #10521 (RF31) — puente de parámetros documentales por gestora hacia el checklist condicional.
        services.AddScoped<Flit.Tramites.Domain.Repositories.IChecklistCompanyParamsProvider, ChecklistCompanyParamsProvider>();
        // HU #10522 (RF17/RF22) — puente de la matriz documental resuelta del gestor hacia el checklist (matriz viva).
        services.AddScoped<Flit.Tramites.Domain.Repositories.IResolvedChecklistMatrixProvider, Services.ResolvedChecklistMatrixProvider>();
        // CF-06 (HU #10881) — override OT del documento de prenda (independiente del semáforo de gravámenes),
        // SNAPSHOT: solo overrides activos antes de crear el trámite.
        services.AddScoped<Flit.Tramites.Domain.Repositories.IPrendaDocumentRequirementPolicy, Services.PrendaDocumentRequirementPolicy>();
        // HU #10522 (RF40) — política de validación por IA de improntas (por defecto: advertir).
        services.Configure<Flit.Tramites.Application.UseCases.ProcedureInstances.ImprontaValidationPolicyOptions>(
            configuration.GetSection(
                Flit.Tramites.Application.UseCases.ProcedureInstances.ImprontaValidationPolicyOptions.SectionName));
        // Se expone el POCO resuelto para que Application (IdentityValidationResultApplier) lo consuma
        // sin depender de Microsoft.Extensions.Options.
        services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<Flit.Tramites.Application.UseCases.ProcedureInstances.ImprontaValidationPolicyOptions>>().Value);

        // ── Dashboard analítico (Feature #10139, HU #10243/#10245) ───────────
        services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();
        services.AddScoped<IAnalyticsMetricsReadRepository, AnalyticsMetricsReadRepository>(); // Reportes2 HU-B
        services.AddScoped<Flit.Analytics.Application.Abstractions.IDetailedReportReadRepository, DetailedReportReadRepository>(); // Feature #10813
        services.AddScoped<Flit.Analytics.Application.Queries.IDetailedReportExcelExporter, Documents.DetailedReportExcelExporter>(); // Feature #10813 HU #10816
        services.AddScoped<IProcedureExcelExporter, Documents.ProcedureExcelExporter>();
        services.AddSingleton<IExecutiveSummaryPdfGenerator, Documents.ExecutiveSummaryPdfGenerator>();

        // Reportes2 HU-D — informes programados + alertas por umbral (scheduler y repos).
        services.AddScoped<Flit.Analytics.Application.Scheduling.IReportScheduleRepository, ReportScheduleRepository>(); // Reportes2 HU-D
        services.AddScoped<Flit.Analytics.Application.Scheduling.IAlertRuleRepository, AlertRuleRepository>(); // Reportes2 HU-D
        services.AddScoped<Flit.Analytics.Application.Scheduling.IAlertMetricsReadRepository, Analytics.Scheduling.AlertMetricsReadRepository>(); // Reportes2 HU-D
        services.AddHostedService<Analytics.Scheduling.AnalyticsSchedulerProcessor>(); // Reportes2 HU-D

        services.Configure<Telemetry.AnalyticsTelemetryOptions>(configuration.GetSection(Telemetry.AnalyticsTelemetryOptions.SectionName)); // Reportes2 HU-A
        services.AddSingleton<Telemetry.ChannelUsageEventQueue>(); // Reportes2 HU-A
        services.AddSingleton<Telemetry.IUsageEventQueue>(sp => sp.GetRequiredService<Telemetry.ChannelUsageEventQueue>()); // Reportes2 HU-A
        services.AddHostedService<Telemetry.UsageEventWriterProcessor>(); // Reportes2 HU-A
        services.AddScoped<IUsageMetricsReadRepository, UsageMetricsReadRepository>(); // Reportes2 HU-A

        AddAttachmentStorage(services, configuration);

        // HU #10256 — FUR por overlay PdfSharpCore sobre plantillas blank.
        services.AddSingleton<IFurDocumentGenerator, FurOverlayDocumentGenerator>();
        // HU #10919 (Feature #10918) — plantilla de FUR según la clasificación del vehículo (catálogo
        // tramites.vehicle_classification_fur). Singleton: cachea el catálogo una sola vez.
        services.AddSingleton<IFurTemplateResolver, Documents.Fur.VehicleClassificationFurResolver>();
        services.AddSingleton<IExpedienteConsolidadoMerger, PdfExpedienteConsolidadoMerger>();
        // HU #10458 — certificado de identidad en PDF real (QuestPDF). Reemplaza el mock text/plain
        // para que pase IsMergeableMime y se fusione en el Expediente Consolidado.
        services.AddSingleton<IIdentityCertificateGenerator, Documents.IdentityCertificatePdfGenerator>();
        services.AddSingleton<IRuesCertificateGenerator, Documents.RuesCertificatePdfGenerator>();
        // HU #10926 (ADR-0033) — resolutor de escrituras vigentes por actor NIT para adjuntarlas al
        // consolidado. Scoped: depende de los readers de escrituras/directorio (DbContext) + storage.
        services.AddScoped<Flit.Tramites.Application.Documents.IProcedureDeedResolver, Documents.ProcedureDeedResolver>();
        // HU #10762 — certificado RNMC suelto (PDF real) con el resultado de medidas correctivas por parte.
        services.AddSingleton<IRnmcCertificateGenerator, Documents.RnmcCertificatePdfGenerator>();
        // ADR-0036 (HU #10914) — Solicitud de trámite de forma virtual (PDF real, siempre).
        services.AddSingleton<ISolicitudVirtualGenerator, Documents.SolicitudVirtualPdfGenerator>();
        // ADR-0036 (HU #10915) — Contrato Privado de Mandato (PDF real, condicional por OT/persona).
        services.AddSingleton<IMandatoGenerator, Documents.MandatoPdfGenerator>();
        // HU #10856 — certificados de vigencia SOAT/RTM (PDF real con membrete FLIT) desde el RUNT.
        services.AddSingleton<ISoatRtmCertificateGenerator, Documents.SoatRtmCertificatePdfGenerator>();

        AddConsultationProviders(services, configuration);
        AddIdentityValidation(services, configuration);
        AddImprontas(services, configuration);
        AddRues(services, configuration);
        AddOcr(services, configuration);
        AddQuipux(services);

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

        // HU #10621 — Editar nombre/correo de un usuario; HU #10619 — repositorio compartido de
        // suspensión/desactivación/reactivación de usuarios (mismo repositorio, IUserManagementRepository).
        services.AddScoped<IUserManagementRepository, UserManagementRepository>();

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
            o.VerifikRuesMode = Cfg("Consultations:VerifikRuesMode", "VERIFIK_RUES_MODE") ?? "mock";
            o.IntempoMode = Cfg("Consultations:IntempoMode", "INTEMPO_MODE") ?? "mock";
            o.FasecoldaMode = Cfg("Consultations:FasecoldaMode", "FASECOLDA_MODE") ?? "mock";
            // FEATURE 05 — comparendos. Ambos en mock por defecto: ver ConsultationProviderModeOptions.
            o.FlitFinesMode = Cfg("Consultations:FlitFinesMode", "FLIT_FINES_MODE") ?? "mock";
            o.KyverumFinesMode = Cfg("Consultations:KyverumFinesMode", "KYVERUM_FINES_MODE") ?? "mock";
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

        // FEATURE 05 — API de registro de FLIT (fuente interna de comparendos). Sin credenciales.
        services.Configure<FlitRegistrationApiOptions>(o =>
        {
            o.BaseUrl = Cfg("RegistrationApi:BaseUrl", "REGISTRATION_API_BASE_URL")
                        ?? "https://knli4dcix0.execute-api.us-east-1.amazonaws.com/pdn";
            o.InfractionPath = Cfg("RegistrationApi:InfractionPath", "REGISTRATION_API_INFRACTION_PATH")
                        ?? "api/v1/registration/simit";
            o.TimeoutSeconds = int.TryParse(Cfg("RegistrationApi:TimeoutSeconds", "REGISTRATION_API_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        // FEATURE 05 — KYVERUM comparendos (persona jurídica). URL/ruta provisionales; en mock
        // hasta que el proveedor entregue especificación y credenciales.
        services.Configure<KyverumFinesOptions>(o =>
        {
            o.BaseUrl = Cfg("KyverumFines:BaseUrl", "KYVERUM_FINES_BASE_URL") ?? "https://runt.kyverum.com";
            o.InfractionPath = Cfg("KyverumFines:InfractionPath", "KYVERUM_FINES_INFRACTION_PATH") ?? "/v1/comparendos:consultar";
            o.ApiKey = Cfg("KyverumFines:ApiKey", "KYVERUM_FINES_API_KEY") ?? "";
            o.AuthScheme = Cfg("KyverumFines:AuthScheme", "KYVERUM_FINES_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("KyverumFines:TimeoutSeconds", "KYVERUM_FINES_TIMEOUT_SECONDS"), out var t) ? t : 30;
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

        services.AddHttpClient<VerifikRuesConsultationProvider>((sp, c) =>
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

        // FEATURE 05 — fuente interna de comparendos. NormalizedBaseUrl conserva la barra final:
        // el BaseUrl trae el stage del API Gateway (/pdn) y sin ella la ruta relativa lo descarta.
        services.AddHttpClient<FlitFinesConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<FlitRegistrationApiOptions>>().Value;
            c.BaseAddress = new Uri(o.NormalizedBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // FEATURE 05 — KYVERUM comparendos (persona jurídica). Config propia, no la del RUNT.
        services.AddHttpClient<KyverumFinesConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<KyverumFinesOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Kyverum RUNT (HU #10478): cliente de consultas compartido, mismo config que improntas
        // (ImprontaRuntOptions / KYVERUM_RUNT_*, configurado en AddImprontas). Los providers
        // kyverum_runt / kyverum_runt_conductor lo consumen; convergen al mismo ConsultationResult
        // que Verifik para ser intercambiables en la cadena de proveedores (Fase 3).
        services.AddHttpClient<KyverumRuntApiClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<ImprontaRuntOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Proveedores expuestos como IConsultationProvider para el registry.
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikSimitConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikRnmcConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConductorConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikRuesConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<IntempoConsultationProvider>());
        services.AddTransient<IConsultationProvider, KyverumRuntVehicleConsultationProvider>();
        services.AddTransient<IConsultationProvider, KyverumRuntConductorConsultationProvider>();
        // FEATURE 05 — comparendos por fuente. Quedan registrados pero SIN TRÁFICO hasta HU10758,
        // que es la que cablea fines_query_source al preflight y empieza a resolverlos.
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<FlitFinesConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<KyverumFinesConsultationProvider>());
        services.AddSingleton<IConsultationProvider, FlitIntegrationsGatewayProvider>();
        services.AddScoped<IConsultationProviderRegistry, ConsultationProviderRegistry>();

        // Cadena de proveedores Kyverum-first con fallback a Verifik (HU #10478, Fase 3). Defaults en
        // appsettings (sección Consultations:DefaultChains / FailoverTimeoutMs); si faltan, el propio
        // ConsultationChainOptions embebe el orden del plan. Aún no lo consumen los handlers (Fase 5).
        services.Configure<ConsultationChainOptions>(o =>
            configuration.GetSection(ConsultationChainOptions.SectionName).Bind(o));
        services.AddScoped<IConsultationProviderChainResolver>(sp =>
            new ConsultationProviderChainResolver(
                sp.GetRequiredService<IConsultationProviderRegistry>(),
                sp.GetRequiredService<IOptions<ConsultationChainOptions>>().Value));

        // Puente tenant → override de cadena/timeout (HU #10478, Fase 5). Lee
        // admin.tenant_operational_policies vía ITenantSettingsRepository.
        services.AddScoped<IConsultationTenantOverrideProvider, TenantConsultationOverrideProvider>();

        // Avalúo comercial multi-proveedor (Feature #10707, ADR-0029): capa aparte de la de
        // consultas (verificación) — agrega VALOR de varias fuentes en paralelo.
        AddAvaluoProviders(services, configuration);
    }

    private static void AddAvaluoProviders(IServiceCollection services, IConfiguration configuration)
    {
        string? Cfg(string key, string env) =>
            configuration[key] ?? Environment.GetEnvironmentVariable(env);

        services.Configure<FasecoldaOptions>(o =>
        {
            o.ByVinBaseUrl = Cfg("Fasecolda:ByVinBaseUrl", "FASECOLDA_BY_VIN_API_BASE_URL") ?? o.ByVinBaseUrl;
            o.ByVinPath = Cfg("Fasecolda:ByVinPath", "FASECOLDA_BY_VIN_API_PATH") ?? o.ByVinPath;
            o.ApiBaseUrl = Cfg("Fasecolda:ApiBaseUrl", "FASECOLDA_API_BASE_URL") ?? o.ApiBaseUrl;
            o.AuthPath = Cfg("Fasecolda:AuthPath", "FASECOLDA_AUTH_API_PATH") ?? o.AuthPath;
            o.ListCodePath = Cfg("Fasecolda:ListCodePath", "FASECOLDA_LIST_CODE_API_PATH") ?? o.ListCodePath;
            o.GrantType = Cfg("Fasecolda:GrantType", "FASECOLDA_API_GRANT_TYPE") ?? o.GrantType;
            o.Username = Cfg("Fasecolda:Username", "FASECOLDA_API_USERNAME") ?? "";
            o.Password = Cfg("Fasecolda:Password", "FASECOLDA_API_PASSWORD") ?? "";
            o.TimeoutSeconds = int.TryParse(Cfg("Fasecolda:TimeoutSeconds", "FASECOLDA_API_SECONDS_TIMEOUT"), out var t) ? t : o.TimeoutSeconds;
        });

        // Dos hosts (búsqueda por VIN sin auth; guía de valores con token). Clientes con nombre.
        services.AddHttpClient("fasecolda-vin", (sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<FasecoldaOptions>>().Value;
            c.BaseAddress = new Uri(o.ByVinBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.AddHttpClient("fasecolda-api", (sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<FasecoldaOptions>>().Value;
            c.BaseAddress = new Uri(o.ApiBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddSingleton<FasecoldaTokenCache>();
        services.AddScoped<AvaluoMockValueReader>();
        services.AddScoped<IAvaluoProvider, FasecoldaAvaluoProvider>();
        // Fase 1: mock, activables por configuración a real sin tocar el handler (ADR-0029).
        services.AddScoped<IAvaluoProvider, BaseGravableAvaluoProvider>();
        services.AddScoped<IAvaluoProvider, MercadoLibreAvaluoProvider>();
        services.AddScoped<IAvaluoProviderRegistry, AvaluoProviderRegistry>();
        // Feature #10707 — proveedores habilitados por tenant (lee tenant_operational_policies).
        services.AddScoped<IAvaluoProviderPolicy, TenantAvaluoPolicyProvider>();
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

        // Descarga del certificado de la validación (PDF) desde la API pública de Kyverum
        // (GET /v1/validations/{id}/certificado). Reusa el MISMO Bearer API key que el create — sin cookie
        // ni login admin (el panel /admin/api exige MFA y no aplica para integración server-to-server).
        services.AddHttpClient<IKyverumCertificateClient, KyverumCertificateClient>((sp, c) =>
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
        // Bitácora ÚNICA del ciclo de identidad (envío/webhook/descifrado/errores). Escribe en su propio
        // scope, así queda registrada aunque el webhook termine en 500/401.
        services.AddScoped<IIdentityValidationAuditLog, IdentityValidationAuditLog>();

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
        // Red de seguridad: reconcilia por consulta las validaciones en_proceso colgadas (webhook perdido).
        services.AddHostedService<IdentityValidationReconcileProcessor>();

        // HU-3 (N03): puerto de publicación del lifecycle de estados. Encola en
        // procedure_state_change_outbox (misma unidad de trabajo del lifecycle service); el worker
        // despacha las filas pendientes hacia IProcedureStateChangeNotifier (webhooks OT) tras el commit.
        services.AddScoped<ITramiteTransitionPublisher, ProcedureStateChangeOutboxPublisher>();
        services.AddHostedService<ProcedureStateChangeOutboxProcessor>();

        // Plano C (ICT §A.3/§A.9): reflejo de estado hacia core-ict. Añade el sink ICT al notifier
        // COMPUESTO (junto a los webhooks OT) cuando hay Ict:StateCallback:Address; sin endpoint es no-op.
        services.AddIctStateReflection(configuration);
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

    private static void AddRues(IServiceCollection services, IConfiguration configuration)
    {
        // RF36 — autogeneración del Certificado RUES. Opt-in: solo se registra el cliente HTTP cuando
        // Rues:Enabled=true y hay BaseUrl. Sin registro, GenerarRuesAttachmentHandler recibe el cliente
        // opcional en null y responde "rues_autogen_disabled" (respaldo: carga manual). Env var CRUDA
        // primero (override 12-factor), fallback a configuration. La API key NUNCA se loguea.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        var enabled = string.Equals(Cfg("Rues:Enabled", "RUES_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
        var baseUrl = Cfg("Rues:BaseUrl", "RUES_BASE_URL");
        if (!enabled || string.IsNullOrWhiteSpace(baseUrl))
            return;

        services.Configure<RuesOptions>(o =>
        {
            o.Enabled = true;
            o.BaseUrl = baseUrl;
            o.ApiKey = Cfg("Rues:ApiKey", "RUES_API_KEY") ?? "";
            o.AuthScheme = Cfg("Rues:AuthScheme", "RUES_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("Rues:TimeoutSeconds", "RUES_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        services.AddHttpClient<IRuesExternalClient, RuesApiClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<RuesOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
    }

    /// <summary>
    /// Integración Quipux: radicación de trámites en las secretarías de tránsito.
    /// </summary>
    /// <remarks>
    /// <para>A diferencia del resto de integraciones, <b>no recibe <see cref="IConfiguration"/> ni
    /// tiene gate de registro</b>. La configuración de Quipux (credenciales, URLs, cadencia) vive
    /// en <c>admin.quipux_settings</c>, no en appsettings/env vars, por requisito explícito:
    /// rotar una credencial o cambiar el intervalo debe ser un UPDATE, sin desplegar. Por eso todo
    /// se registra siempre y el gate real es <c>settings.Enabled</c>, releído por los workers en
    /// cada ciclo. Sin fila o con <c>enabled = false</c> la integración es inerte.</para>
    /// <para>El corolario es que el patrón de "no registrar el cliente y dejar la dependencia en
    /// null" (el de RUES) aquí no aplica: no se puede decidir en el arranque algo que la BD puede
    /// cambiar en caliente.</para>
    /// <para>Tampoco se usa <c>IsDevelopment()</c> como gate de mock/real: el compose de PDN corre
    /// con <c>ASPNETCORE_ENVIRONMENT=Development</c>.</para>
    /// </remarks>
    private static void AddQuipux(IServiceCollection services)
    {
        // Los handlers del módulo. Se registran aquí —y no en Program.cs— igual que
        // AddSecurityApplication(): quien consume estos handlers son los workers de este mismo
        // ensamblado, así que registrarlos juntos evita que un módulo quede a medio cablear.
        // Sin esta línea todo COMPILA pero los workers revientan en el primer ciclo al resolverlos.
        services.AddQuipuxApplication();

        // Configuración y secretos. El protector cifra password_enc / aws_secret_access_key_enc con
        // Data Protection (keyring ya persistido en Postgres): el claro nunca toca la BD.
        services.AddSingleton<IQuipuxSecretProtector, DataProtectionQuipuxSecretProtector>();
        services.AddScoped<IQuipuxSettingsRepository, QuipuxSettingsRepository>();

        // Estado de la radicación y trazabilidad.
        services.AddScoped<IQuipuxSubmissionRepository, QuipuxSubmissionRepository>();

        // Consola de cola QX (HU #10774): lectura por secretaría destino + acciones manuales. Puerto
        // aparte del de los workers — sin claim/lease, con filtro explícito por transit_office_id.
        services.AddScoped<IQuipuxSubmissionConsoleRepository, DbQuipuxSubmissionConsoleRepository>();

        // LOG QX (HU #10793): lectura de trazabilidad para soporte/admin. Solo consulta (sin claim ni
        // transiciones), cross-tenant por el mismo motivo que la consola de cola.
        services.AddScoped<IQuipuxLogRepository, DbQuipuxLogRepository>();
        services.AddSingleton<IQuipuxAuditLog, QuipuxSubmissionAuditLog>();
        services.AddSingleton<IQuipuxJobRunLog, QuipuxJobRunLog>();

        // Adaptadores de los puertos que declara Quipux.Application, para que el módulo no dependa
        // de Tramites.Application ni del DbContext.
        services.AddScoped<IQuipuxConsolidadoMaestroPort, QuipuxConsolidadoMaestroAdapter>();
        services.AddScoped<IQuipuxOrganismoPort, QuipuxOrganismoAdapter>();
        services.AddScoped<IQuipuxTenantPort, QuipuxTenantAdapter>();

        // Publicación del PDF en el bucket S3 DE QUIPUX. Scoped: resuelve el adjunto vía DbContext.
        services.AddScoped<IQuipuxDocumentUploader, QuipuxS3DocumentUploader>();

        // Cliente HTTP. Sin BaseAddress: las URLs son absolutas y salen de la BD en cada llamada,
        // porque un BaseAddress fijado aquí se congelaría en el arranque y no podría cambiar en
        // caliente. El Timeout sí queda fijado (limitación de HttpClient) con un valor holgado.
        services.AddHttpClient<IQuipuxClient, QuipuxApiClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(120));

        // Los dos workers (el "cron"). ADR-0024 rechaza cron/broker externo: van dentro de core-api
        // con claim FOR UPDATE SKIP LOCKED. Registrados siempre; el gate es la BD.
        services.AddHostedService<QuipuxRegisterProcessor>();
        services.AddHostedService<QuipuxStatusPollProcessor>();
    }

    private static void AddOcr(IServiceCollection services, IConfiguration configuration)
    {
        // OCR semántico de documentos de trámites. Env var CRUDA primero (override 12-factor),
        // fallback a configuration — mismo orden y motivo que el resto de integraciones externas.
        // La API key NUNCA se loguea.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        services.Configure<AnthropicOptions>(o =>
        {
            o.BaseUrl = Cfg("Anthropic:BaseUrl", "ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com";
            o.ApiKey = Cfg("Anthropic:ApiKey", "ANTHROPIC_API_KEY") ?? "";
            o.Model = Cfg("Anthropic:Model", "ANTHROPIC_MODEL") ?? "claude-haiku-4-5-20251001";
            o.TimeoutSeconds = int.TryParse(Cfg("Anthropic:TimeoutSeconds", "ANTHROPIC_TIMEOUT_SECONDS"), out var t) ? t : 60;
            o.MaxTokens = int.TryParse(Cfg("Anthropic:MaxTokens", "ANTHROPIC_MAX_TOKENS"), out var m) ? m : 2000;
        });

        // Typed HttpClient (compatible con PublishAot, como Verifik/Kyverum).
        services.AddHttpClient<AnthropicMessagesClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.AddScoped<AnthropicDocumentOcrAnalyzer>();

        // Recorte de páginas de PDFs multi-documento (PdfSharpCore). Stateless ⇒ singleton. El handler
        // (Application) lo usa tras el análisis para devolver sólo el subconjunto de páginas del tipo.
        services.AddSingleton<IPdfPageExtractor, PdfSharpPageExtractor>();

        // Feature flag de proveedor (mock por defecto ⇒ no rompe dev/CI sin API key). Mismo patrón que
        // BiometricsProviderOptions / ConsultationProviderModeOptions. El MockDocumentOcrAnalyzer vive en
        // Application; el handler (AnalyzeDocumentHandler) se registra en Application DI y no cambia.
        var provider = Cfg("Ocr:Provider", "OCR_PROVIDER") ?? "mock";
        if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IDocumentOcrAnalyzer>(sp => sp.GetRequiredService<AnthropicDocumentOcrAnalyzer>());
        else
            services.AddScoped<IDocumentOcrAnalyzer, MockDocumentOcrAnalyzer>();
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
