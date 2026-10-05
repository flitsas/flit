using Flit.Api.Endpoints;
using Flit.Api.Endpoints.Analytics;
using Flit.Api.Endpoints.Internal;
using Flit.Api.Endpoints.Public;
using Flit.Api.Endpoints.SuperAdmin;
using Flit.Api.Endpoints.Tramites;

namespace Flit.Api.Hosting;

/// <summary>
/// Rutas de negocio de <c>core-api</c>: todo menos <see cref="IdentityHosting.MapIdentityEndpoints"/>, que mapean los
/// dos papeles (HU #13224). Se movieron tal cual desde <c>Program.cs</c>.
/// </summary>
internal static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        // Orquestación ICT (core-ict -> core-api): exige el service-token (esquema/policy IctService) para que
        // solo core-ict autenticado como sistema pueda invocar la orquestación (no un tercero en el puerto interno).
        app.MapGrpcService<Flit.Api.Grpc.IctOrchestrationService>()
            .RequireAuthorization(Flit.Api.Authorization.ApiSecurityExtensions.IctServicePolicy);
        // Consulta de fuentes externas para ICT (reusa el subsistema de consultas de core-api).
        app.MapGrpcService<Flit.Api.Grpc.IctConsultationService>()
            .RequireAuthorization(Flit.Api.Authorization.ApiSecurityExtensions.IctServicePolicy);

        // ── Endpoints de seguridad + Admin/parametrización (develop) ──────────────────
        app.MapExternalAuthEndpoints(); // HU #13087 (Épica #12737) — POST /api/v1/external/auth/token
        app.MapExternalSyncEndpoints(); // HU #13081 (Épica #12737) — GET /api/v1/external/tramites/sync
        app.MapSecurityEndpoints();
        app.MapUserUiPreferencesEndpoints();
        app.MapDrFlitEndpoints(); // Épica #12718 — POST /api/v1/dr-flit/chat
        app.MapAdminCompaniesEndpoints();
        app.MapAdminCompaniesBrandingEndpoints();
        app.MapCompanyBrandingEndpoints();
        app.MapAdminCompaniesDomainEndpoints();
        app.MapCompanyDomainEndpoints();
        app.MapInternalDomainsEndpoints();
        app.MapAdminCompanyChildrenEndpoints();
        app.MapAdminCompanyChildrenConfigEndpoints();
        app.MapAdminCompanyChildrenInvitationsEndpoints();
        app.MapAdminOtEndpoints();
        app.MapAdminOtMetricsEndpoints();
        app.MapAdminOtQueriesEndpoints();
        app.MapAdminPlateRangesEndpoints();
        app.MapOtIntegrationEndpoints();
        app.MapAdminTransitOfficesEndpoints();
        app.MapAdminQuipuxEndpoints();
        app.MapAdminIctJobSettingsEndpoints();
        app.MapAdminIctJobCatalogEndpoints();
        app.MapAdminPlataformaMandatosEndpoints();
        app.MapAdminOtMandatosEndpoints();
        app.MapAdminPlataformaFurEndpoints();
        app.MapAdminHierarchySwitchesEndpoints(); // HU #12323 — interruptores globales de jerarquía (SuperAdmin)
        app.MapAdminPlataformaNotificacionesEndpoints();
        app.MapAdminPlataformaNotificacionesPlantillasEndpoints();
        app.MapAdminRuntConfirmationEndpoints();
        app.MapAdminLogQxEndpoints();
        app.MapAdminTransitOfficeTenantsEndpoints();
        app.MapAdminMandateSignersEndpoints();
        app.MapAdminCompanyMandateSignersEndpoints();
        app.MapAdminMandateSignerIdentityEndpoints();
        app.MapAdminSignatureVaultEndpoints();
        app.MapAdminLegalRepresentativesEndpoints();
        app.MapAdminDeedsEndpoints();
        app.MapAdminPersonalizedDocumentsEndpoints();
        app.MapAdminCompanyNotificationDeliveryLogsEndpoints();
        app.MapAdminLegalRepresentativeIdentityEndpoints();
        app.MapAdminIdentityVigenciaEndpoints();
        app.MapAdminDocumentTypesEndpoints();
        app.MapAdminBannersEndpoints();
        app.MapAdminExternalClientsEndpoints(); // HU #13088 (Épica #12737) — clientes de integración externos (SuperAdmin)
        app.MapAdminRejectionReasonsEndpoints();
        app.MapAdminProcedureDocumentRequirementsEndpoints();
        app.MapAdminDocumentOrderOverridesEndpoints();
        app.MapAdminDocumentRequirementOverridesEndpoints();
        app.MapAdminOtPrendaDocumentPolicyEndpoints();
        app.MapAdminResolvedDocumentMatrixEndpoints();
        app.MapAdminCompanyDocumentParamsEndpoints();
        app.MapAdminImprontasEndpoints();
        // Feature #12201 (ADR-0056-generacion-documental-standalone) — generación documental SIN trámite.
        // Autorización por permiso (generacion-documental.*), no por policy de grupo.
        app.MapAdminGeneracionDocumentalEndpoints();
        app.MapTramitesEndpoints();
        app.MapBulkTramitesEndpoints();
        // Epic #12543 — aceptación de Términos y Condiciones antes de abrir el asistente.
        app.MapTramitesTermsAcceptanceEndpoints();
        app.MapTransfersEndpoints();

        // ── Runtime de trámites (rework #10128) ───────────────────────────────────────
        app.MapSuperAdminEndpoints();
        app.MapPublicProcedureEndpoints();
        app.MapPublicProcedureTypeEndpoints();
        app.MapPublicBiometricaEndpoints();
        app.MapPublicKyverumWebhookEndpoints();
        app.MapPublicPortalEndpoints();
        // HU #12240 (Feature #12236) — banners promocionales: listado publico + imagen por streaming.
        app.MapPublicBannersEndpoints();
        // HU #12418 — /api/v1/me/branding (la marca pública va en MapIdentityEndpoints).
        app.MapMeBrandingEndpoints();
        app.MapTramitesInstanceEndpoints();
        // HU #12358 (Feature #12257) — vista consolidada de la red (solo lectura) bajo /api/v1/tramites/network.
        app.MapTramitesNetworkEndpoints();
        // HU #12361 (Feature #12257) — consulta de la auditoría de accesos consolidados (hijo + SuperAdmin).
        app.MapNetworkAccessAuditEndpoints();
        app.MapTramitesActorEndpoints();
        // HU #11196 / #11197 — firma a posteriori: marcar el trámite y consultar si la opción aplica.
        app.MapTramitesFirmaPosteriorEndpoints();
        app.MapTramitesAttachmentEndpoints();
        app.MapTramitesRevocationRequestEndpoints(); // HU #12572 (Feature #12565) — solicitud de revocatoria de trámite Aprobado
        app.MapTramitesOcrEndpoints();
        app.MapTramitesParticipantEndpoints();
        app.MapTramitesBiometricaEndpoints();
        app.MapTramitesFirmaEndpoints();
        app.MapTramitesFurEndpoints();
        app.MapTramitesConsolidadoEndpoints();
        app.MapAdminTramiteConsolidadoEndpoints(); // HU #12158 — limpiar/cargar consolidado (admin)
        app.MapAdminTramiteEstadoEndpoints(); // HU #12159 — cambiar estado sin restricción de flujo (admin)
        app.MapAdminTramiteAnularEndpoints(); // HU #12160 — anular desde cualquier estado salvo Aprobado/Revocado
        app.MapAdminTramiteReenviarValidacionEndpoints(); // HU #12161 — reenviar validación de identidad (admin, correo opcional)
        app.MapAdminTramiteReasignarGestorEndpoints(); // HU #12162 — reasignar gestor (AssignedToUserId) + selector de disponibles
        app.MapConsultationEndpoints();
        app.MapTramitesCommercialEndpoints();
        app.MapTramitesPreflightEndpoints();
        app.MapTramitesRnmcEndpoints();
        app.MapTramitesWizardEndpoints();
        app.MapTramitesVehicleColorsEndpoints();
        app.MapTramitesVehicleBodyworksEndpoints();
        app.MapTramitesVehicleServiceTypesEndpoints();
        app.MapTramitesStatusHistoryEndpoints();
        app.MapTramitesNotificationDispatchesEndpoints();
        app.MapLegalRepresentativeConsumptionEndpoints();

        // ── Dashboard analítico (Feature #10139) ──────────────────────────────────────
        app.MapAnalyticsEndpoints();
        app.MapDashboardActiveModulesEndpoints(); // HU #12251 (Feature #12249) — flags de módulos activos, sin AdminCompanyPolicy
        app.MapDetailedReportEndpoints(); // Feature #10813
        app.MapReportSchedulesEndpoints(); // Reportes2 HU-D
        app.MapSuperAdminReportSchedulesEndpoints(); // Reportes2 HU-D 2da ola — informes de consulta SuperAdmin
        app.MapAlertRulesEndpoints(); // Reportes2 HU-D
        app.MapAdminOtReportSchedulesEndpoints(); // Reportes2 HU-D 3ra ola — informes programados del OT
        app.MapAdminOtAlertRulesEndpoints(); // Reportes2 HU-D 3ra ola — alertas por umbral del OT
        app.MapAnalyticsMetricsEndpoints(); // Reportes2 HU-B
        app.MapCompanyQueriesEndpoints(); // Consultas propias de la empresa
        app.MapSuperAdminQueriesEndpoints(); // Consultas de SuperAdmin sobre todas las compañías
        app.MapIctQueriesEndpoints(); // Consultas propias de la empresa sobre sus pre-trámites de ICT
        app.MapIctReportsEndpoints(); // Reportes de ICT en vivo (HU #11617)
        app.MapUsageEventsEndpoints(); // Reportes2 HU-A

        return app;
    }
}
