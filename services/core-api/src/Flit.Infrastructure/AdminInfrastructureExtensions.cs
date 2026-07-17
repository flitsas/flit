using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Companies.VehicleOwnership;
using Flit.Admin.Domain.Companies.Whitelist;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.DocumentTypes;
using Flit.Admin.Domain.Improntas;
using Flit.Admin.Domain.OtProfile;
using Flit.Admin.Domain.OtRequirements;
using Flit.Admin.Domain.OtWebhooks;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtDocumentPrecedence;
using Flit.Admin.Domain.OtDocumentTags;
using Flit.Admin.Domain.OtRules;
using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.OtWebhooks;
using Flit.Tramites.Domain.Integration;
using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure;

/// <summary>
/// Registro de las dependencias de persistencia del módulo Admin (HU #10189, #10190, #10191).
/// </summary>
public static class AdminInfrastructureExtensions
{
    public static IServiceCollection AddAdminInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // RNF01 (ADR-0024) — auditoría mínima de configuración: acceso a IP/usuario de la
        // petición (sin acoplar Application a HTTP) + writer de fallos en scope independiente.
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditContextAccessor, HttpAuditContextAccessor>();
        services.AddScoped<IAuditFailureWriter, AuditFailureWriter>();

        services.AddScoped<ICompanyReadRepository, CompanyReadRepository>();
        services.AddScoped<ICompanyWriteRepository, CompanyWriteRepository>();
        services.AddScoped<ITenantSettingsRepository, TenantSettingsRepository>();

        // HU #10191 — lista blanca + checker de propiedad vehicular (stub transitorio).
        services.AddScoped<IWhitelistRepository, WhitelistRepository>();
        services.AddScoped<IVehicleTenantOwnershipChecker, StubVehicleTenantOwnershipChecker>();

        // HU #10192 — grants de organismos de tránsito + catálogo OT desde BD.
        services.AddScoped<ITransitOfficeCatalog, DbTransitOfficeCatalog>();
        services.AddScoped<ITransitGrantRepository, TransitGrantRepository>();
        services.AddScoped<ITenantAuditLogRepository, TenantAuditLogRepository>();

        // Refactor adminOT — alta/listado de tenants Organismo de Tránsito (OT como
        // tenant de primera clase: tenant + rol ot_admin + perfil OT en una operación).
        services.AddScoped<ITransitOfficeTenantWriteRepository, TransitOfficeTenantWriteRepository>();

        // RF01 — estado operativo del catálogo OT (catálogo LEFT JOIN perfil + tenant),
        // lectura cross-tenant para el listado del SuperAdmin.
        services.AddScoped<ITransitOfficeOperationalStatusReader, DbTransitOfficeOperationalStatusReader>();

        // HU #10710 — parametrización Quipux de la secretaría DESTINO (code_divipo + banderas).
        // Escritura sobre el catálogo global catalogs.transit_offices (sin RLS), solo SuperAdmin.
        services.AddScoped<ITransitOfficeQuipuxSettingsWriter, DbTransitOfficeQuipuxSettingsWriter>();

        // ADR-0023 — mandatarios (firmantes de mandato) por OT: lectura cross-tenant +
        // escritura con auditoría atómica (RF22–RF28).
        services.AddScoped<IMandateSignerReader, DbMandateSignerReader>();
        services.AddScoped<IMandateSignerRepository, MandateSignerRepository>();

        // HU #10193 — catálogo de tipos de documento (CRUD SuperAdmin).
        services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();

        // HU #10195 — asociación documentos ↔ tipos de trámite + catálogo de trámites
        // (read-only). El guard de uso es ahora la implementación real (HU #10197).
        services.AddScoped<IProcedureDocumentRequirementRepository, ProcedureDocumentRequirementRepository>();
        services.AddScoped<IProcedureTypeCatalog, ProcedureTypeCatalog>();
        services.AddScoped<IProcedureDocumentRequirementUsageGuard, ProcedureDocumentRequirementUsageGuard>();

        // HU #10196 — overrides de orden documental (OT/Cliente) + catálogo de clientes
        // (read-only) + resolutor de la matriz documental (precedencia Cliente > OT > Default).
        services.AddScoped<IDocumentOrderOverrideRepository, DocumentOrderOverrideRepository>();
        services.AddScoped<ITenantCatalog, TenantCatalog>();
        services.AddScoped<IResolvedDocumentMatrixResolver, ResolvedDocumentMatrixResolver>();

        // HU #10198 — overrides de obligatoriedad documental por OT (3 estados).
        services.AddScoped<IDocumentRequirementOverrideRepository, DocumentRequirementOverrideRepository>();

        // HU #10521 (RF31) — parámetros documentales por compañía gestora.
        services.AddScoped<Flit.Admin.Domain.CompanyDocumentParams.ICompanyDocumentParamRepository,
            CompanyDocumentParamRepository>();

        // HU #10197 — instancias de trámite + snapshot documental inmutable.
        // Tras el merge del rework (#10128) la implementación vive en
        // AdminProcedureInstanceRepository (opera sobre la entidad canónica del runtime).
        services.AddScoped<IProcedureInstanceRepository, AdminProcedureInstanceRepository>();
        services.AddScoped<IProcedureDocumentSnapshotRepository, ProcedureDocumentSnapshotRepository>();

        // HU #10215 — perfil OT y feature flags.
        services.AddScoped<IOtProfileRepository, OtProfileRepository>();
        services.AddScoped<IOtFeatureFlagRepository, OtFeatureFlagRepository>();

        // HU #10545 — requisitos configurables por OT + provider con defaults seguros.
        services.AddScoped<IOtRequirementsRepository, OtRequirementsRepository>();
        services.AddScoped<IOtRequirementsProvider, OtRequirementsProvider>();

        // HU #10216 — webhooks OT, bitácora API y dispatch de cambios de estado.
        services.AddScoped<IOtWebhookSubscriptionRepository, OtWebhookSubscriptionRepository>();
        services.AddScoped<IOtApiCallLogRepository, OtApiCallLogRepository>();
        services.AddScoped<IOtWebhookSecretHasher, OtWebhookSecretHasherService>();
        services.AddScoped<IOtWebhookDispatchService, OtWebhookDispatchService>();
        services.AddScoped<IProcedureStateChangeNotifier, OtWebhookProcedureStateChangeNotifier>();

        services.AddHttpClient(nameof(OtWebhookDispatchService), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // HU #10217 — trámites de clientes OT (cross-tenant vía grants).
        services.AddScoped<IOtClientProcedureRepository, OtClientProcedureRepository>();

        // HU #10221 — motor de reglas OT (sobre ot_feature_flags) + runtime gate.
        services.AddScoped<IOtRuleRepository, OtRuleRepository>();
        services.AddScoped<IOtRuleGate, OtRuleGateService>();

        // #2 — validación de OT habilitado por empresa en el submit de trámites.
        services.AddScoped<ITransitOfficeGrantGate, TransitOfficeGrantGate>();

        // HU #10518 — enforcement runtime del ciclo de vida OT: el OT elegido debe estar
        // OPERATIVO (catálogo activo + perfil/tenant OT + tenant activo), no solo con grant.
        services.AddScoped<IOtOperabilityGate, OtOperabilityGate>();

        // HU #10548 — exigibilidad de la validación de identidad según la config del OT destino.
        services.AddScoped<IIdentityValidationPolicy, IdentityValidationPolicy>();

        // HU #10602 — exigibilidad de la consulta RNMC según la config del OT destino (requires_rnmc).
        services.AddScoped<IRnmcRequirementPolicy, RnmcRequirementPolicy>();

        // B11 (HU #10659) — en traspaso el OT lo fija el RUNT: resuelve el OT habilitado de la
        // empresa por nombre (grants + catálogo) para poblar transit_office_id en el preflight.
        services.AddScoped<ITransitOfficeResolver, TransitOfficeResolver>();

        // HU #10222 — prelación documental y etiquetas OT.
        services.AddScoped<IOtDocumentPrecedenceRepository, OtDocumentPrecedenceRepository>();
        services.AddScoped<IOtDocumentTagRepository, OtDocumentTagRepository>();

        // HU #10466 — historial de improntas generadas (ADR-0022).
        services.AddScoped<IImprontaRepository, ImprontaRepository>();

        return services;
    }
}
