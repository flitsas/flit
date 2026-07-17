using Flit.Admin.Application.Auditing.GetAdminAuditLog;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.ListCompanies;
using Flit.Admin.Application.Companies.SetCompanyStatus;
using Flit.Admin.Application.Companies.UpdateCompany;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Admin.Application.Companies.TransitOffices;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;
using Flit.Admin.Application.Companies.TransitOffices.CreateTransitOffice;
using Flit.Admin.Application.Companies.TransitOffices.GetOtBlockingPolicies;
using Flit.Admin.Application.Companies.TransitOffices.GetOtConsultationRestrictions;
using Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;
using Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;
using Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficeTenants;
using Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficesOperationalStatus;
using Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;
using Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;
using Flit.Admin.Application.Companies.TransitOffices.SetOtBlockingPolicy;
using Flit.Admin.Application.Companies.TransitOffices.SetOtConsultationRestriction;
using Flit.Admin.Application.Companies.TransitOffices.SetTransitOfficeTenantStatus;
using Flit.Admin.Application.Companies.TransitOffices.UpdateTransitOfficeQuipuxSettings;
using Flit.Admin.Application.Companies.VehicleOwnership;
using Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;
using Flit.Admin.Application.Companies.Whitelist.GetWhitelist;
using Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.GetResolvedDocumentMatrix;
using Flit.Admin.Application.DocumentOrderOverrides.ListDocumentOrderOverrides;
using Flit.Admin.Application.DocumentOrderOverrides.UpdateDocumentOrderOverride;
using Flit.Admin.Application.DocumentRequirementOverrides.ListDocumentRequirementOverrides;
using Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;
using Flit.Admin.Application.DocumentRequirements.CreateProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentRequirements.DeleteProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentRequirements.ListProcedureDocumentRequirements;
using Flit.Admin.Application.DocumentRequirements.UpdateProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentTypes.CreateDocumentType;
using Flit.Admin.Application.DocumentTypes.DeleteDocumentType;
using Flit.Admin.Application.DocumentTypes.ListDocumentTypes;
using Flit.Admin.Application.DocumentTypes.ReactivateDocumentType;
using Flit.Admin.Application.DocumentTypes.UpdateDocumentType;
using Flit.Admin.Application.Improntas.GenerarImpronta;
using Flit.Admin.Application.Improntas.ListImprontas;
using Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;
using Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;
using Flit.Admin.Application.OtProfile;
using Flit.Admin.Application.OtProfile.GetOtProfile;
using Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;
using Flit.Admin.Application.OtProfile.UpdateOtProfile;
using Flit.Admin.Application.OtRequirements.GetOtRequirements;
using Flit.Admin.Application.OtRequirements.UpdateOtRequirements;
using Flit.Admin.Application.OtWebhooks.CreateOtWebhook;
using Flit.Admin.Application.OtWebhooks.ListOtApiLogs;
using Flit.Admin.Application.OtWebhooks.ListOtWebhooks;
using Flit.Admin.Application.OtWebhooks.ProcessOtWebhookCallback;
using Flit.Admin.Application.OtWebhooks.UpdateOtWebhook;
using Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.GetOtBandejaHealth;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;
using Flit.Admin.Application.OtDocumentPrecedence.ListOtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentPrecedence.UpdateOtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentTags.CreateOtDocumentTag;
using Flit.Admin.Application.OtDocumentTags.DeleteOtDocumentTag;
using Flit.Admin.Application.OtDocumentTags.ListOtDocumentTags;
using Flit.Admin.Application.OtRules;
using Flit.Admin.Application.OtRules.CreateOtRule;
using Flit.Admin.Application.OtRules.ListOtRules;
using Flit.Admin.Application.OtRules.UpdateOtRule;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.OtProfile;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Companies.VehicleOwnership;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Admin.Application;

/// <summary>
/// Registro de los casos de uso del módulo Admin en el contenedor DI.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAdminApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // HU #10189 — listado de compañías.
        services.AddScoped<ListCompaniesHandler>();

        // Alta de compañías (botón "Crear compañía" en la consola, #10118).
        services.AddScoped<CreateCompanyHandler>();

        // Activar/desactivar compañía (toggle de estado en el listado, #10118).
        services.AddScoped<SetCompanyStatusHandler>();

        // Editar compañía (botón "Editar" en el listado, #10118).
        services.AddScoped<UpdateCompanyHandler>();

        // HU #10190 — configuración operativa + audit log.
        services.AddScoped<GetTenantSettingsHandler>();
        services.AddScoped<UpdateTenantSettingsHandler>();
        services.AddSingleton<ITenantPolicyResolver, SnapshotTenantPolicyResolver>();

        // HU #10191 — interceptor propiedad vehicular + API whitelist.
        services.AddScoped<IVehicleOwnershipGuard, VehicleOwnershipGuard>();
        services.AddScoped<AddWhitelistEmailsHandler>();
        services.AddScoped<GetWhitelistHandler>();

        // HU #10192 — catálogo OT (BD) + grants + consulta audit log.
        // ITransitOfficeCatalog se registra en AddAdminInfrastructure (DbTransitOfficeCatalog).
        services.AddScoped<SearchTransitOfficesHandler>();
        // RF01 — estado operativo del catálogo OT (join catálogo + perfil + tenant).
        // ITransitOfficeOperationalStatusReader se registra en AddAdminInfrastructure.
        services.AddScoped<ListTransitOfficesOperationalStatusHandler>();
        // HU #10710 — parametrización Quipux de la secretaría DESTINO (code_divipo + banderas
        // por familia de trámite). ITransitOfficeQuipuxSettingsWriter se registra en
        // AddAdminInfrastructure.
        services.AddScoped<UpdateTransitOfficeQuipuxSettingsHandler>();
        services.AddScoped<AddTransitGrantHandler>();
        services.AddScoped<RemoveTransitGrantHandler>();

        // Refactor adminOT — alta/listado de tenants OT.
        // ITransitOfficeTenantWriteRepository se registra en AddAdminInfrastructure.
        services.AddScoped<CreateTransitOfficeHandler>();
        services.AddScoped<ListTransitOfficeTenantsHandler>();
        // HU #10518 — cambio de estado del tenant OT con auditoría atómica.
        services.AddScoped<SetTransitOfficeTenantStatusHandler>();
        services.AddScoped<GetTransitGrantsHandler>();
        services.AddScoped<GetTenantAuditLogHandler>();

        // HU #10759 — restricciones de consulta (RNMC, comparendos) por OT de la compañía.
        // IOtConsultationRestrictionRepository se registra en AddAdminInfrastructure.
        services.AddScoped<GetOtConsultationRestrictionsHandler>();
        services.AddScoped<SetOtConsultationRestrictionHandler>();

        // FEATURE 05 — política de bloqueo de preflight por criterio y OT.
        // IOtBlockingPolicyRepository se registra en AddAdminInfrastructure.
        services.AddScoped<GetOtBlockingPoliciesHandler>();
        services.AddScoped<SetOtBlockingPolicyHandler>();

        // HU #10679 — consulta global (cross-tenant, SuperAdmin) del rastro unificado de
        // auditoría administrativa/seguridad. IAdminAuditLogRepository se registra en
        // AddAdminInfrastructure.
        services.AddScoped<GetAdminAuditLogHandler>();

        // ADR-0023 — mandatarios (firmantes de mandato) por OT: CRUD (RF22–RF27), regla de
        // uso RF33 y vista consolidada RF34. IMandateSignerReader/Repository → AddAdminInfrastructure.
        services.AddScoped<CreateMandateSignerHandler>();
        services.AddScoped<UpdateMandateSignerHandler>();
        services.AddScoped<InactivateMandateSignerHandler>();
        services.AddScoped<ReactivateMandateSignerHandler>();
        services.AddScoped<ListMandateSignersHandler>();
        services.AddScoped<ListOtCompaniesHandler>();

        // HU #10468 — listado paginado/filtrable del historial de improntas (ADR-0022).
        // IImprontaRepository se registra en AddAdminInfrastructure.
        services.AddScoped<ListImprontasHandler>();

        // HU #10193 — catálogo de tipos de documento (CRUD SuperAdmin).
        services.AddScoped<CreateDocumentTypeHandler>();
        services.AddScoped<ListDocumentTypesHandler>();
        services.AddScoped<UpdateDocumentTypeHandler>();
        services.AddScoped<DeleteDocumentTypeHandler>();
        services.AddScoped<ReactivateDocumentTypeHandler>();

        // HU #10195 — asociación de documentos a tipos de trámite (CRUD SuperAdmin).
        services.AddScoped<CreateProcedureDocumentRequirementHandler>();
        services.AddScoped<ListProcedureDocumentRequirementsHandler>();
        services.AddScoped<UpdateProcedureDocumentRequirementHandler>();
        services.AddScoped<DeleteProcedureDocumentRequirementHandler>();

        // HU #10196 — overrides de orden documental (OT/Cliente) + matriz resuelta.
        services.AddScoped<CreateDocumentOrderOverrideHandler>();
        services.AddScoped<ListDocumentOrderOverridesHandler>();
        services.AddScoped<UpdateDocumentOrderOverrideHandler>();
        services.AddScoped<DeleteDocumentOrderOverrideHandler>();
        services.AddScoped<GetResolvedDocumentMatrixHandler>();

        // HU #10198 — obligatoriedad documental por OT (3 estados, granular solo para OT).
        services.AddScoped<SetDocumentRequirementOverrideHandler>();
        services.AddScoped<ListDocumentRequirementOverridesHandler>();

        // HU #10521 (RF31) — parámetros documentales por compañía gestora.
        services.AddScoped<CompanyDocumentParams.ListCompanyDocumentParamsHandler>();
        services.AddScoped<CompanyDocumentParams.UpsertCompanyDocumentParamHandler>();

        // HU #10197 — alta de trámite con snapshot documental inmutable + lectura del snapshot.
        services.AddScoped<CreateProcedureInstanceHandler>();
        services.AddScoped<GetProcedureDocumentRequirementsHandler>();

        // HU #10215 — perfil OT, modo Dashboard/QX y feature flags.
        services.AddScoped<GetOtProfileHandler>();
        services.AddScoped<UpdateOtProfileHandler>();
        services.AddScoped<UpdateOtFeatureFlagHandler>();
        services.AddScoped<IQuipuxReadOnlyGuard, QuipuxReadOnlyGuard>();

        // HU #10546 — requisitos configurables por OT (RNMC, ruta de placa, validación de identidad).
        services.AddScoped<GetOtRequirementsHandler>();
        services.AddScoped<UpdateOtRequirementsHandler>();

        // HU #10216 — webhooks OT y bitácora API.
        services.AddScoped<CreateOtWebhookHandler>();
        services.AddScoped<UpdateOtWebhookHandler>();
        services.AddScoped<ListOtApiLogsHandler>();
        services.AddScoped<ListOtWebhooksHandler>();
        services.AddScoped<ProcessOtWebhookCallbackHandler>();

        // HU #10217 — trámites de clientes OT (tenant admin).
        services.AddScoped<ListOtClientProceduresHandler>();
        services.AddScoped<GetOtClientProcedureHandler>();
        services.AddScoped<ApproveOtClientProcedureHandler>();
        services.AddScoped<RejectOtClientProcedureHandler>();
        // HU #10540 (R09) — diagnóstico de bandeja OT (entregados con/sin grant).
        services.AddScoped<GetOtBandejaHealthHandler>();

        // HU #10221 — motor de reglas AND/OR.
        services.AddScoped<CreateOtRuleHandler>();
        services.AddScoped<UpdateOtRuleHandler>();
        services.AddScoped<ListOtRulesHandler>();

        // HU #10222 — prelación documental y etiquetas.
        services.AddScoped<ListOtDocumentPrecedenceHandler>();
        services.AddScoped<UpdateOtDocumentPrecedenceHandler>();
        services.AddScoped<CreateOtDocumentTagHandler>();
        services.AddScoped<DeleteOtDocumentTagHandler>();
        services.AddScoped<ListOtDocumentTagsHandler>();

        // HU #10467 — generación/persistencia/entrega del PDF de impronta (Kyverum RUNT).
        // IImprontaExternalClient (HU #10465) e IImprontaRepository (HU #10466) se registran en
        // Flit.Infrastructure (InfrastructureExtensions/AddAdminInfrastructure).
        services.AddScoped<GenerarImprontaHandler>();

        return services;
    }
}
