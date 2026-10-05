using Flit.Admin.Domain.Companies.Settings;
using Flit.Analytics.Application.Abstractions;
using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Consultations.Avaluos;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Fur;
using Flit.Infrastructure.DrFlit;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Ict;
using Flit.Infrastructure.Improntas;
using Flit.Infrastructure.KyverumRunt;
using Flit.Infrastructure.Rues;
using Flit.Infrastructure.Kyverum;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Notifications.DeliveryLog;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Infrastructure.Ocr;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Platform;
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
using Flit.Modules.Security.Domain.UiPreferences;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        // Mismas convenciones que el contexto de core-identity (HU #13231): NpgsqlConventions.
        services.AddDbContext<FlitDbContext>((serviceProvider, opts) =>
            NpgsqlConventions.Apply(opts, connectionString));
        // Epic #13217 (HU #13231): los repositorios de identidad dependen de IIdentityDb; en core-api es el mismo contexto.
        services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<FlitDbContext>());

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
        // Validación del SOAT contra el RUNT al procesar, activable por compañía.
        services.AddScoped<Flit.Tramites.Application.UseCases.ProcedureInstances.ISoatRuntValidationPolicy,
            OtRules.SoatRuntValidationPolicy>();
        services.AddScoped<IProcedureInstanceRepository, ProcedureInstanceRepository>();
        // Bug #13194 (review PR #510, MAYOR-2) — savepoint por trámite en el lote del outbox de identidad.
        services.AddScoped<ISavepointScope, Persistence.EfSavepointScope>();
        // HU #12358 — dueño de un trámite por id, solo para el guard de escritura de la red (TenantWriteGuard).
        services.AddScoped<IProcedureInstanceOwnerLookup, ProcedureInstanceOwnerLookup>();
        // HU #12361 - auditoria del acceso consolidado (tramites.network_access_audit): escritura
        // best-effort con scope propio y lectura para el hijo / SuperAdmin.
        services.AddScoped<Flit.Tramites.Application.Auditing.INetworkAccessAuditWriter, Auditing.NetworkAccessAuditWriter>();
        services.AddScoped<Flit.Tramites.Application.Auditing.INetworkAccessAuditReader, Auditing.NetworkAccessAuditReader>();
        // HU #11196 — marcas de firma a posteriori (el lote que se firma cuando el representante valida).
        services.AddScoped<Flit.Tramites.Domain.Repositories.IDeferredSignatureMarkRepository,
            DeferredSignatureMarkRepository>();
        services.AddScoped<Flit.Tramites.Domain.Repositories.IVehicleSignatureImprintRepository,
            VehicleSignatureImprintRepository>();
        services.AddScoped<Flit.Tramites.Domain.Repositories.IImprintSignatureValidationRepository,
            ImprintSignatureValidationRepository>();
        // IT-3 (Feature #10585) — persistencia del agregado de prenda.
        services.AddScoped<IProcedureInstancePrendaRepository, ProcedureInstancePrendaRepository>();
        // HU #12571 (Feature #12565) — persistencia de solicitudes de revocatoria de trámite Aprobado.
        services.AddScoped<IProcedureRevocationRequestRepository, ProcedureRevocationRequestRepository>();
        // HU #12572/#12576/#12579 (Feature #12565) — sink del sub-flujo de revocatoria: bitácora +
        // cola real de correo (ver XML doc de la clase).
        services.AddScoped<Flit.Tramites.Domain.Integration.IRevocationRequestNotifier,
            RevocationRequestNotificationEnqueuer>();
        services.AddScoped<IIdentityValidationOutboxRepository, IdentityValidationOutboxRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        // HU #12520 (Feature #12519) — plantillas XLSX de carga masiva de trámites.
        services.AddScoped<
            Flit.Tramites.Application.BulkTramites.IBulkTramitesXlsxTemplate,
            Flit.Infrastructure.Documents.BulkTramites.BulkTramitesXlsxTemplate>();
        // HU #12522 — persistencia del lote de carga masiva y su parser XLSX.
        services.AddScoped<
            Flit.Tramites.Domain.Repositories.IBulkTramitesBatchRepository,
            Flit.Infrastructure.Persistence.Repositories.BulkTramitesBatchRepository>();
        services.AddScoped<
            Flit.Tramites.Application.BulkTramites.IBulkTramitesXlsxParser,
            Flit.Infrastructure.Documents.BulkTramites.BulkTramitesXlsxParser>();
        // HU #10878 (Feature #10862, CF-04) — caché cross-trámite de consultas externas (ADR-0030)
        // + gate de consentimiento Habeas Data para el reúso de datos de persona (ADR-0031).
        services.AddScoped<Flit.Tramites.Domain.Repositories.IExternalQueryCacheRepository, ExternalQueryCacheRepository>();
        services.AddScoped<Flit.Tramites.Domain.Repositories.IPersonDataConsentRepository, PersonDataConsentRepository>();
        // HU #11302 (Feature #11301, ADR-0041) — almacén propio de certificaciones externas
        // (SOAT, RTM y registro mercantil) en modelo canónico, con payload crudo para reprocesar.
        services.AddScoped<Flit.Tramites.Application.UseCases.Certifications.ICertificationRepository,
            CertificationRepository>();
        // Feature #12276 — Confirmación RUNT: configuración global (HU #12277) y su auditoría sobre el
        // rastro administrativo unificado (IAdminAuditWriter), sin tabla de auditoría nueva.
        services.AddScoped<Flit.Tramites.Application.UseCases.RuntConfirmation.IRuntConfirmationSettingsRepository,
            RuntConfirmation.RuntConfirmationSettingsRepository>();
        services.AddScoped<Flit.Tramites.Application.UseCases.RuntConfirmation.IRuntConfirmationAuditWriter,
            RuntConfirmation.RuntConfirmationAuditWriter>();
        // Epic #12543 — aceptación de T&C antes de crear trámite: fila de evidencia (hard-fail) +
        // reflejo en el rastro administrativo unificado. La URL del documento sale de ProcedureTerms:Url.
        services.AddSingleton(new Flit.Tramites.Application.UseCases.TermsAcceptance.ProcedureTermsOptions
        {
            Url = string.IsNullOrWhiteSpace(configuration["ProcedureTerms:Url"])
                ? Flit.Tramites.Application.UseCases.TermsAcceptance.ProcedureTermsOptions.DefaultUrl
                : configuration["ProcedureTerms:Url"]!.Trim(),
        });
        services.AddScoped<Flit.Tramites.Application.UseCases.TermsAcceptance.IProcedureTermsAcceptanceRepository,
            TermsAcceptance.ProcedureTermsAcceptanceRepository>();
        services.AddScoped<Flit.Tramites.Application.UseCases.TermsAcceptance.IProcedureTermsAcceptanceAuditWriter,
            TermsAcceptance.ProcedureTermsAcceptanceAuditWriter>();
        // HU #12309 — almacén con scope propio por operación (la corrida graba en paralelo), consumidor
        // propio del RUNT según providerKey (sin pasar por la cadena de proveedores del wizard) y el
        // programador diario. Registrado siempre; el gate es la fila de configuración en BD.
        services.AddSingleton<Flit.Tramites.Application.UseCases.RuntConfirmation.IRuntConfirmationStore,
            RuntConfirmation.RuntConfirmationStore>();
        services.AddSingleton(new Flit.Tramites.Application.UseCases.RuntConfirmation.RuntConfirmationRunnerOptions
        {
            MaxConcurrency = int.TryParse(configuration["RuntConfirmation:MaxConcurrency"], out var rcMax) && rcMax > 0 ? rcMax : 4,
            StaleRunAfterHours = int.TryParse(configuration["RuntConfirmation:StaleRunAfterHours"], out var rcStale) && rcStale > 0 ? rcStale : 6,
            MaxCandidatesPerRun = int.TryParse(configuration["RuntConfirmation:MaxCandidatesPerRun"], out var rcCap) && rcCap > 0 ? rcCap : 5000,
        });
        services.AddHttpClient<RuntConfirmation.VerifikRuntRawHttpClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.AddScoped<Flit.Tramites.Application.UseCases.RuntConfirmation.IRuntVehicleRawClient,
            RuntConfirmation.RuntVehicleRawClient>();
        services.AddHostedService<RuntConfirmation.RuntConfirmationSchedulerProcessor>();
        // HU #12310 — lecturas del Historial (cross-tenant, pantalla de plataforma).
        services.AddScoped<Flit.Tramites.Application.UseCases.RuntConfirmation.IRuntConfirmationHistoryReader,
            RuntConfirmation.RuntConfirmationHistoryReader>();
        // HU #10865 — entidad persona/sujeto a nivel tenant (Feature #10864, CF-00, ADR-0030).
        services.AddScoped<Flit.Tramites.Domain.Repositories.IPersonRepository, PersonRepository>();
        // HU #10520 — catálogo de tipos de documento para validación de carga por tipo (MIME/tamaño).
        services.AddScoped<Flit.Tramites.Domain.Tramites.Catalog.IDocumentTypeCatalog, DocumentTypeCatalog>();
        // Catálogo RUNT de colores de vehículo (transformaciones FUR) — búsqueda paginada.
        services.AddScoped<Flit.Tramites.Domain.Tramites.Catalog.IVehicleColorCatalog, DbVehicleColorCatalog>();
        services.AddScoped<Flit.Tramites.Domain.Tramites.Catalog.IVehicleBodyworkCatalog, DbVehicleBodyworkCatalog>();
        // Catálogo global de tipos de servicio del vehículo (sección 18 del FUR, ADR-0019) — cerrado, 6 valores.
        services.AddScoped<Flit.Tramites.Domain.Tramites.Catalog.IVehicleServiceTypeCatalog, DbVehicleServiceTypeCatalog>();
        // HU #10521 (RF31) — puente de parámetros documentales por gestora hacia el checklist condicional.
        services.AddScoped<Flit.Tramites.Domain.Repositories.IChecklistCompanyParamsProvider, ChecklistCompanyParamsProvider>();
        // HU #10522 (RF17/RF22) — puente de la matriz documental resuelta del gestor hacia el checklist (matriz viva).
        services.AddScoped<Flit.Tramites.Domain.Repositories.IResolvedChecklistMatrixProvider, Services.ResolvedChecklistMatrixProvider>();
        // HU #11184 — orden del expediente configurado por el OT (admin.ot_document_precedence).
        // Vacío = el OT no configuró nada ⇒ el consolidado conserva el orden por modalidad.
        services.AddScoped<Flit.Tramites.Domain.Repositories.IOtConfiguredDocumentOrderProvider, Services.OtConfiguredDocumentOrderProvider>();
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

        // HU #10970 — modo por ambiente de CF-01 (duplicidad) y CF-03 (precondición registral):
        // block (default fail-safe) / warn / off. Se configura por el .env de cada VPS
        // (TramiteValidations__<Validación>__Mode) porque DEV, QA y PDN corren TODOS con
        // ASPNETCORE_ENVIRONMENT=Development y appsettings.{Environment}.json no los distingue.
        services.AddTramiteValidationPolicy(configuration);

        // ── Dashboard analítico (Feature #10139, HU #10243/#10245) ───────────
        services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();
        // HU #13076 (Épica #12737) — lectura entre compañías del feed de sincronización externa (ámbito exclusivo).
        services.AddScoped<Flit.Tramites.Domain.ExternalSync.IProcedureSyncReadRepository, ProcedureSyncReadRepository>();
        services.AddScoped<INetworkAnalyticsReadRepository, AnalyticsNetworkReadRepository>(); // HU #12359 - estadisticas de red
        services.AddScoped<IAnalyticsMetricsReadRepository, AnalyticsMetricsReadRepository>(); // Reportes2 HU-B
        services.AddScoped<Flit.Analytics.Application.Abstractions.IDetailedReportReadRepository, DetailedReportReadRepository>(); // Feature #10813
        services.AddScoped<Flit.Analytics.Application.Queries.IDetailedReportExcelExporter, Documents.DetailedReportExcelExporter>(); // Feature #10813 HU #10816
        services.AddScoped<Flit.Analytics.Application.Abstractions.INetworkDetailedReportReadRepository, DetailedReportNetworkReadRepository>(); // HU #12360 - reporte de red
        services.AddScoped<Flit.Analytics.Application.CompanyQueries.ICompanyQueryRepository, CompanyQueryRepository>();
        services.AddScoped<Flit.Analytics.Application.CompanyQueries.ISuperAdminSavedQueryRepository, SuperAdminSavedQueryRepository>();
        services.AddScoped<Flit.Analytics.Application.IctQueries.IIctQueryRepository, IctQueryRepository>();
        services.AddScoped<IProcedureExcelExporter, Documents.ProcedureExcelExporter>();
        services.AddSingleton<IExecutiveSummaryPdfGenerator, Documents.ExecutiveSummaryPdfGenerator>();
        services.AddScoped<Analytics.Scheduling.UsageReportDocumentBuilder>(); // Reportes2 HU-D
        services.AddScoped<Analytics.Scheduling.OtReportDocumentBuilder>(); // Reportes2 HU-D
        services.AddScoped<Analytics.Scheduling.OtOwnReportDocumentBuilder>(); // Reportes2 HU-D, alcance OT
        services.AddScoped<Analytics.Scheduling.OtQueryReportDocumentBuilder>(); // Reportes2 HU-D, alcance OT
        services.AddScoped<Analytics.Scheduling.CompanyQueryReportDocumentBuilder>(); // Reportes2 HU-D 2da ola
        services.AddScoped<Analytics.Scheduling.IctOwnReportDocumentBuilder>(); // Reportes2 HU-D, alcance ICT
        services.AddScoped<Analytics.Scheduling.IctQueryReportDocumentBuilder>(); // Reportes2 HU-D, consulta alcance ICT

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
        services.AddSingleton<IImprontaManualStamper, Documents.Improntas.ImprontaManualStamper>();
        services.AddSingleton<Flit.Tramites.Application.Documents.IImprontaManualSignatureVerifier,
            Documents.Improntas.ImprontaManualSignatureVerifier>();
        // HU #10458 — certificado de identidad en PDF real (QuestPDF). Reemplaza el mock text/plain
        // para que pase IsMergeableMime y se fusione en el Expediente Consolidado.
        services.AddSingleton<IIdentityCertificateGenerator, Documents.IdentityCertificatePdfGenerator>();
        services.AddSingleton<IRuesCertificateGenerator, Documents.RuesCertificatePdfGenerator>();
        // HU #10926 (ADR-0033) — resolutor de escrituras vigentes por actor NIT para adjuntarlas al
        // consolidado. Scoped: depende de los readers de escrituras/directorio (DbContext) + storage.
        services.AddScoped<Flit.Tramites.Application.Documents.IProcedureDeedResolver, Documents.ProcedureDeedResolver>();
        // HU #11316 (Feature #11309, ADR-0042) — ÚNICO punto de sustitución por documento personalizado
        // de compañía. Lista de tipos habilitados VACÍA hasta las HUs #11317/#11318 (ver la clase).
        services.AddScoped<Flit.Tramites.Application.Documents.IPersonalizedDocumentResolver, Documents.PersonalizedDocumentResolver>();
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
        services.AddRentingChannel(configuration); // HU #13232: vive en Flit.Identity.Infrastructure
        AddOcr(services, configuration);
        AddDrFlit(services, configuration, environment);
        AddQuipux(services);

        // ── Seguridad / login (HU #10168, #10169) — Epic #13217 (HU #13232): lo que comparte con core-identity vive en
        // Flit.Identity.Infrastructure (IdentityInfrastructureExtensions); aquí quedan los CRUD de seguridad de core-api.
        services.AddIdentityLoginServices(configuration, environment);

        // HU #13087 — pase de los clientes de integración externos (llave, emisor y audiencia propios). Solo core-api.
        services.AddExternalClientAuth(configuration);

        // HU #10161 — CRUD módulos dinámicos Super Admin
        services.AddScoped<ISecurityModuleRepository, SecurityModuleRepository>();
        // HU #10162 — CRUD permisos granulares Super Admin
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        // HU #10163 — CRUD roles y asociación de permisos Super Admin
        services.AddScoped<IRoleRepository, RoleRepository>();
        // HU #10621 — Editar nombre/correo de un usuario; HU #10619 — repositorio compartido de
        // suspensión/desactivación/reactivación de usuarios (mismo repositorio, IUserManagementRepository).
        services.AddScoped<IUserManagementRepository, UserManagementRepository>();
        // Preferencias de UI por usuario (base compartida: elegir columnas visibles en tablas).
        services.AddScoped<IUserUiPreferenceRepository, UserUiPreferenceRepository>();

        services.AddSecurityApplication();

        // === FLIT Suite: infraestructura ===
        // Una línea por frente que llama a Add<Modulo>Infrastructure(), definido en un archivo
        // propio (regla R5 de docs/suite/reglas-trabajo-paralelo.md).
        services.AddPlatformInfrastructure(); // Frente B · HU #12958
        // === FLIT Suite: fin infraestructura ===

        return services;
    }

    private static void AddAttachmentStorage(IServiceCollection services, IConfiguration configuration)
    {
        // Adjuntos en el file-manager de la empresa (S3 vía presigned URLs). Sin disco, sin
        // credenciales AWS en flit. Config primero (appsettings/`FileManager__*`), fallback a
        // env crudas FILE_MANAGER_* (mismo patrón que Verifik/Kyverum).
        services.Configure<FileManagerOptions>(o => FileManagerDownloader.Configure(o, configuration));

        // Typed HttpClient (compatible con PublishAot, como Verifik/Kyverum). El BaseAddress apunta
        // al file-manager; las subidas/descargas a S3 usan la presigned URL absoluta (lo ignora).
        services.AddHttpClient<IAttachmentStorage, FileManagerAttachmentStorage>((sp, c) =>
            FileManagerDownloader.ConfigureClient(
                c, sp.GetRequiredService<IOptions<FileManagerOptions>>().Value, "el almacenamiento de adjuntos"));
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
        services.AddSingleton<IIdentitySignatureExtractor, Documents.IdentitySignatureExtractor>();
        services.AddScoped<IIdentitySignatureArtifactStorage, Storage.IdentitySignatureArtifactStorage>();

        // Cifrado del secreto del webhook (AC2/seguridad): Data Protection API.
        // El keyring se persiste en Postgres (tabla data_protection_keys vía FlitDbContext) y se
        // fija un ApplicationName estable: así todas las réplicas comparten las mismas llaves y
        // sobreviven a reinicios. Sin esto, las llaves quedan en el filesystem efímero de cada pod
        // y el secreto HMAC del webhook de Kyverum no se puede descifrar tras un restart/otra réplica.
        // Epic #13217 (HU #13232): mismo nombre de aplicación y misma tabla que core-identity.
        services.AddFlitDataProtection<FlitDbContext>();
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
        // HU #11467 — worker de la cola de avisos de correo al cambio de estado (ADR-0045).
        services.AddHostedService<ProcedureStateChangeEmailDispatchProcessor>();
        // Bug #11613 — traza persistida de los fallos de regeneración documental (aprobar / asignar
        // placa). Escribe con SQL parametrizado, sin pasar por el change tracker del intento fallido.
        services.AddScoped<Flit.Tramites.Application.UseCases.ProcedureInstances.IRegeneracionDocumentalTrazaWriter,
            RegeneracionDocumentalTrazaWriter>();
        // Bug #11612 — compañía radicadora de la portada del consolidado: razón social del tenant
        // dueño del trámite (identity.tenants.legal_name), resuelta siempre por id.
        services.AddScoped<Flit.Tramites.Domain.Integration.ICompaniaRadicadoraDirectory,
            CompaniaRadicadoraDirectory>();
        // HU #11485 (Feature #11482, ADR-0046) — sink post-asignación de placa (Flujo B).
        services.AddScoped<Flit.Tramites.Application.Notifications.IPlateAssignmentEmailEnqueuer,
            PlateAssignmentEmailEnqueuer>();
        // Proyección del modelo y marca FLIT/Renting por canal del tenant (worker plate-assignment).
        services.AddScoped<Flit.Tramites.Application.Notifications.IPlateAssignmentBrandResolver,
            PlateAssignmentBrandResolver>();
        services.AddScoped<Flit.Tramites.Application.Notifications.IPlateAssignmentEmailModelProjector,
            PlateAssignmentEmailModelProjectorService>();
        // HU #13287 (Feature #13280, Épica #13202) — correo con el enlace de captura de la identidad manual, por el mismo
        // canal de correo (IEmailSender) y el mismo tema de marca que el resto de avisos.
        services.AddScoped<Flit.Tramites.Application.Identity.IManualCaptureLinkNotifier,
            Flit.Infrastructure.Notifications.Identity.EmailManualCaptureLinkNotifier>();
        // HU #11487 — worker de la cola de avisos de correo al asignar placa (ADR-0046).
        services.AddHostedService<PlateAssignmentEmailDispatchProcessor>();
        // HU #12579 (Feature #12565, ADR-0046 Opción B extendido) — worker de la cola de avisos de
        // correo por hito del sub-flujo de revocatoria (solicitada|aprobada|rechazada).
        services.AddHostedService<RevocationRequestEmailDispatchProcessor>();
        // HU #12210 (Feature #12201, I3) — worker de lotes XLSX de generación documental. Reclama
        // lotes queued (y los processing atascados: reaper R5) y delega el recorrido en el runner de
        // Application, que invoca los MISMOS handlers de la generación individual.
        services.AddHostedService<StandaloneDocumentBatchProcessor>();
        // HU #12523 (Feature #12519) — worker de lotes de carga masiva de TRÁMITES. Mismo patrón que
        // el de generación documental, pero recorriendo los casos de uso del wizard (consulta de
        // vehículo, creación y actores) fila a fila y en secuencia.
        services.AddHostedService<BulkTramitesBatchProcessorService>();
        // HU #12795 (Épica #12760, D1) — regeneración ANTICIPADA de consolidados: canal acotado en
        // memoria + worker con debounce y coalescing por (tenant, trámite, documento). Sin scheduler
        // externo (ADR-0024). Cada trabajo corre en su propio scope con el tenant de la solicitud.
        services.Configure<ConsolidadoRegeneracionOptions>(
            configuration.GetSection(ConsolidadoRegeneracionOptions.SectionName));
        services.AddSingleton<ChannelConsolidadoRegeneracionQueue>();
        services.AddSingleton<Flit.Tramites.Application.UseCases.ProcedureInstances.IConsolidadoRegeneracionQueue>(
            sp => sp.GetRequiredService<ChannelConsolidadoRegeneracionQueue>());
        services.AddHostedService<ConsolidadoRegeneracionProcessor>();

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

        // Bandeja del LOG QX (HU #11786): universo por TRÁMITE (no por radicación), con los
        // elegibles sin radicar incluidos. SQL crudo — el predicado depende del jsonb external_refs.
        services.AddScoped<IQuipuxBandejaRepository, DbQuipuxBandejaRepository>();

        // Trazabilidad de una radicación (HU #11787): cabecera + eventos para hitos + log paginado.
        services.AddScoped<IQuipuxTrazabilidadRepository, DbQuipuxTrazabilidadRepository>();
        services.AddSingleton<IQuipuxAuditLog, QuipuxSubmissionAuditLog>();
        services.AddSingleton<IQuipuxJobRunLog, QuipuxJobRunLog>();

        // Adaptadores de los puertos que declara Quipux.Application, para que el módulo no dependa
        // de Tramites.Application ni del DbContext.
        services.AddScoped<IQuipuxConsolidadoMaestroPort, QuipuxConsolidadoMaestroAdapter>();
        // HU #12787 (AC2) — maestro radicado ante Quipux, fijo en el servidor (puerto de Trámites).
        services.AddScoped<Flit.Tramites.Application.UseCases.ProcedureInstances.IMaestroRadicadoLookup,
            MaestroRadicadoLookup>();
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

    /// <summary>
    /// Épica #12718 (ADR-0060) — DR. FLIT. El LLM del chat reutiliza el <see cref="AnthropicMessagesClient"/>
    /// y las opciones <c>Anthropic:DrFlit*</c> que registra <see cref="AddOcr"/>. Los casos de soporte (Feature #12915)
    /// leen <c>DrFlit:AzureDevOps</c> y <c>DrFlit:SupportCase:*</c> con fallback a env <c>DR_FLIT_*</c>.
    /// </summary>
    private static void AddDrFlit(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddScoped<IDrFlitChatModel, AnthropicDrFlitChatModel>();
        services.AddScoped<IDrFlitUsageCounter>(sp => new DrFlitUsageCounterRepository(sp.GetRequiredService<FlitDbContext>()));
        services.AddSingleton<IDrFlitChatSettings, DrFlitChatSettings>();

        // HU #12921 — manual desde Content/dr-flit/ del content root. Singleton: se lee una vez. El
        // entorno llega por parámetro (no se resuelve de DI) porque es el mismo que recibe el resto de
        // la infraestructura y así el grafo valida también fuera del host web.
        services.AddSingleton<IDrFlitManualCatalogProvider>(sp => new DrFlitManualCatalogProvider(
            environment, sp.GetRequiredService<ILogger<DrFlitManualCatalogProvider>>()));
        // HU #12923 — Bug en Azure DevOps (FLIT - SOPORTE). Env cruda primero (12-factor), como AddOcr.
        // El PAT es de la cuenta de servicio "Dr. FLIT"; nunca se loguea.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        services.Configure<AzureDevOpsOptions>(o =>
        {
            o.OrganizationUrl = Cfg("DrFlit:AzureDevOps:OrganizationUrl", "DR_FLIT_ADO_ORG_URL") ?? o.OrganizationUrl;
            o.Project = Cfg("DrFlit:AzureDevOps:Project", "DR_FLIT_ADO_PROJECT") ?? o.Project;
            o.Pat = Cfg("DrFlit:AzureDevOps:Pat", "DR_FLIT_ADO_PAT") ?? string.Empty;
            o.TimeoutSeconds = int.TryParse(Cfg("DrFlit:AzureDevOps:TimeoutSeconds", "DR_FLIT_ADO_TIMEOUT_SECONDS"), out var ts) ? ts : o.TimeoutSeconds;
            o.TitlePrefix = configuration["DrFlit:AzureDevOps:TitlePrefix"] ?? o.TitlePrefix;
            o.AssignedTo = Cfg("DrFlit:AzureDevOps:AssignedTo", "DR_FLIT_EMAIL") ?? o.AssignedTo;
        });
        services.Configure<DrFlitFieldMappingOptions>(o =>
        {
            var section = configuration.GetSection(DrFlitFieldMappingOptions.SectionName);
            // Una lista configurada REEMPLAZA el allow-list por defecto: el binder de .NET agregaría al
            // final en vez de sustituir, y así sería imposible quitar un módulo retirado del picklist.
            var modules = section.GetSection(nameof(DrFlitFieldMappingOptions.AffectedModules)).Get<List<string>>();
            section.Bind(o);
            if (modules is { Count: > 0 })
                o.AffectedModules = modules;
        });
        services.AddHttpClient<IDrFlitSupportCaseGateway, AzureDevOpsSupportCaseClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(60)); // cada llamada impone su propio deadline (TimeoutSeconds)

        // HU #12924 — límites de los adjuntos previos y ambiente que se reporta en el caso.
        services.Configure<DrFlitSupportCaseOptions>(o =>
        {
            var section = configuration.GetSection(DrFlitSupportCaseOptions.SectionName);
            var mimes = section.GetSection(nameof(DrFlitSupportCaseOptions.AllowedMimeTypes)).Get<List<string>>();
            section.Bind(o);
            if (mimes is { Count: > 0 })
                o.AllowedMimeTypes = mimes; // reemplaza, no agrega (mismo motivo que AffectedModules)
            o.DeployEnvironment = Cfg("DrFlit:DeployEnvironment", "DR_FLIT_DEPLOY_ENVIRONMENT");
        });
        services.AddSingleton<IDrFlitSupportCaseSettings, DrFlitSupportCaseSettings>();
        services.AddScoped<IDrFlitSupportAttachmentStore, DrFlitSupportAttachmentStore>();
        services.AddScoped<IDrFlitSupportCaseRepository, DrFlitSupportCaseRepository>(); // HU #12925

        // HU #12931 — consentimiento de tratamiento de datos para el chat con IA y los casos de soporte.
        services.Configure<DrFlitConsentOptions>(configuration.GetSection(DrFlitConsentOptions.SectionName));
        services.AddSingleton<IDrFlitConsentSettings, DrFlitConsentSettings>();
        services.AddScoped<IDrFlitConsentStore, DrFlitConsentStore>();
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
            o.Model = Cfg("Anthropic:Model", "ANTHROPIC_MODEL") ?? "claude-haiku-4-5";
            o.TimeoutSeconds = int.TryParse(Cfg("Anthropic:TimeoutSeconds", "ANTHROPIC_TIMEOUT_SECONDS"), out var t) ? t : 60;
            o.MaxTokens = int.TryParse(Cfg("Anthropic:MaxTokens", "ANTHROPIC_MAX_TOKENS"), out var m) ? m : 2000;
            o.ClassifierModel = Cfg("Anthropic:ClassifierModel", "ANTHROPIC_CLASSIFIER_MODEL") ?? "claude-sonnet-5";
            o.ClassifierMaxTokens = int.TryParse(Cfg("Anthropic:ClassifierMaxTokens", "ANTHROPIC_CLASSIFIER_MAX_TOKENS"), out var cm) ? cm : 8000;
            o.ClassifierTimeoutSeconds = int.TryParse(Cfg("Anthropic:ClassifierTimeoutSeconds", "ANTHROPIC_CLASSIFIER_TIMEOUT_SECONDS"), out var ctd) ? ctd : 180;

            // Épica #12718 (ADR-0060) — chat de DR. FLIT sobre el mismo cliente y la misma API key.
            o.DrFlitModel = Cfg("Anthropic:DrFlitModel", "ANTHROPIC_DRFLIT_MODEL") ?? "claude-haiku-4-5";
            o.DrFlitMaxTokens = int.TryParse(Cfg("Anthropic:DrFlitMaxTokens", "ANTHROPIC_DRFLIT_MAX_TOKENS"), out var dm) ? dm : 600;
            o.DrFlitTimeoutSeconds = int.TryParse(Cfg("Anthropic:DrFlitTimeoutSeconds", "ANTHROPIC_DRFLIT_TIMEOUT_SECONDS"), out var dt) ? dt : 20;
            o.DrFlitDailyMessageLimit = int.TryParse(Cfg("Anthropic:DrFlitDailyMessageLimit", "ANTHROPIC_DRFLIT_DAILY_MESSAGE_LIMIT"), out var dl) ? dl : 30;
            o.DrFlitEnabled = !string.Equals(Cfg("Anthropic:DrFlitEnabled", "ANTHROPIC_DRFLIT_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);
        });

        // Typed HttpClient (compatible con PublishAot, como Verifik/Kyverum). El timeout del cliente es
        // el MAYOR de los deadlines (analizador, clasificador y chat de DR. FLIT); cada llamada impone
        // el suyo con un CTS enlazado, así el analizador conserva sus 60s y el clasificador los suyos.
        services.AddHttpClient<AnthropicMessagesClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(
                Math.Max(Math.Max(o.TimeoutSeconds, o.ClassifierTimeoutSeconds), o.DrFlitTimeoutSeconds));
        });
        services.AddScoped<AnthropicDocumentOcrAnalyzer>();
        services.AddScoped<AnthropicDocumentBatchClassifier>();
        services.AddScoped<AnthropicOrientationProbe>();

        // Recorte de páginas de PDFs multi-documento (PdfSharpCore). Stateless ⇒ singleton. El handler
        // (Application) lo usa tras el análisis para devolver sólo el subconjunto de páginas del tipo.
        services.AddSingleton<IPdfPageExtractor, PdfSharpPageExtractor>();

        // Feature flag de proveedor (mock por defecto ⇒ no rompe dev/CI sin API key). Mismo patrón que
        // BiometricsProviderOptions / ConsultationProviderModeOptions. El MockDocumentOcrAnalyzer vive en
        // Application; el handler (AnalyzeDocumentHandler) se registra en Application DI y no cambia.
        var provider = Cfg("Ocr:Provider", "OCR_PROVIDER") ?? "mock";
        if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IDocumentOcrAnalyzer>(sp => sp.GetRequiredService<AnthropicDocumentOcrAnalyzer>());
            services.AddScoped<IDocumentBatchClassifier>(sp => sp.GetRequiredService<AnthropicDocumentBatchClassifier>());

            // HU #12036 — el enderezado SOLO se registra con el proveedor real: con el mock no hay a
            // quién preguntarle si la página está derecha, y el handler lo trata como opcional.
            services.AddScoped<IDocumentOrientationProbe>(sp => sp.GetRequiredService<AnthropicOrientationProbe>());
            services.AddScoped<PdfOrientationNormalizer>();
        }
        else
        {
            services.AddScoped<IDocumentOcrAnalyzer, MockDocumentOcrAnalyzer>();
            services.AddScoped<IDocumentBatchClassifier, MockDocumentBatchClassifier>();
        }
    }

    public static async Task InitializeInfrastructureAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await db.Database.MigrateAsync(cancellationToken);
        await DevelopmentAuthSeeder.SeedAsync(db, hasher, SeedSettings.From(configuration, env), cancellationToken);
    }
}

