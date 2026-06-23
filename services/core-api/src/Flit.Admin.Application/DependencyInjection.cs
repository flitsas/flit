using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.ListCompanies;
using Flit.Admin.Application.Companies.SetCompanyStatus;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Admin.Application.Companies.TransitOffices;
using Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;
using Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;
using Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;
using Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;
using Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;
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
using Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;
using Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;
using Flit.Admin.Application.OtProfile;
using Flit.Admin.Application.OtProfile.GetOtProfile;
using Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;
using Flit.Admin.Application.OtProfile.UpdateOtProfile;
using Flit.Admin.Application.OtWebhooks.CreateOtWebhook;
using Flit.Admin.Application.OtWebhooks.ListOtApiLogs;
using Flit.Admin.Application.OtWebhooks.UpdateOtWebhook;
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

        // HU #10190 — configuración operativa + audit log.
        services.AddScoped<GetTenantSettingsHandler>();
        services.AddScoped<UpdateTenantSettingsHandler>();
        services.AddSingleton<ITenantPolicyResolver, SnapshotTenantPolicyResolver>();

        // HU #10191 — interceptor propiedad vehicular + API whitelist.
        services.AddScoped<IVehicleOwnershipGuard, VehicleOwnershipGuard>();
        services.AddScoped<AddWhitelistEmailsHandler>();
        services.AddScoped<GetWhitelistHandler>();

        // HU #10192 — catálogo OT (estático en memoria) + grants + consulta audit log.
        services.AddSingleton<ITransitOfficeCatalog, StaticTransitOfficeCatalog>();
        services.AddScoped<SearchTransitOfficesHandler>();
        services.AddScoped<AddTransitGrantHandler>();
        services.AddScoped<RemoveTransitGrantHandler>();
        services.AddScoped<GetTransitGrantsHandler>();
        services.AddScoped<GetTenantAuditLogHandler>();

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

        // HU #10197 — alta de trámite con snapshot documental inmutable + lectura del snapshot.
        services.AddScoped<CreateProcedureInstanceHandler>();
        services.AddScoped<GetProcedureDocumentRequirementsHandler>();

        // HU #10215 — perfil OT, modo Dashboard/QX y feature flags.
        services.AddScoped<GetOtProfileHandler>();
        services.AddScoped<UpdateOtProfileHandler>();
        services.AddScoped<UpdateOtFeatureFlagHandler>();
        services.AddScoped<IQuipuxReadOnlyGuard, QuipuxReadOnlyGuard>();

        // HU #10216 — webhooks OT y bitácora API.
        services.AddScoped<CreateOtWebhookHandler>();
        services.AddScoped<UpdateOtWebhookHandler>();
        services.AddScoped<ListOtApiLogsHandler>();

        return services;
    }
}
