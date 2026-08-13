using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Quipux;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence;

public sealed class FlitDbContext(DbContextOptions<FlitDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // Keyring de ASP.NET Data Protection persistido en Postgres (HU #10233): compartido entre
    // réplicas y estable entre reinicios, para poder descifrar el secreto HMAC del webhook Kyverum.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<UserTempSuspension> UserTempSuspensions => Set<UserTempSuspension>();

    // ── Seguridad / RBAC (develop) ────────────────────────────────────────────
    public DbSet<SecurityModule> SecurityModules => Set<SecurityModule>();

    public DbSet<RbacAction> RbacActions => Set<RbacAction>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();

    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();

    // HU #10506 — soporte multi-rol: tabla puente N:M invitación-roles.
    public DbSet<InvitationRole> InvitationRoles => Set<InvitationRole>();

    public DbSet<TenantOperationalPolicy> TenantOperationalPolicies => Set<TenantOperationalPolicy>();

    public DbSet<TenantConfigAuditLog> TenantConfigAuditLogs => Set<TenantConfigAuditLog>();

    public DbSet<TenantWhitelistUser> TenantWhitelistUsers => Set<TenantWhitelistUser>();

    public DbSet<TenantTransitOfficeGrant> TenantTransitOfficeGrants => Set<TenantTransitOfficeGrant>();

    /// <summary>
    /// Convenio comercial compañía ↔ organismo. Distinto del grant de arriba, que es el permiso para
    /// radicar: este solo decide si el mandato lleva bloque de firma del mandatario.
    /// </summary>
    public DbSet<CompanyTransitOfficeAgreement> CompanyTransitOfficeAgreements =>
        Set<CompanyTransitOfficeAgreement>();

    // HU #10759 — restricciones de consulta (RNMC, comparendos) por OT de la compañía.
    public DbSet<TenantTransitOfficeConsultationRestriction> TenantTransitOfficeConsultationRestrictions =>
        Set<TenantTransitOfficeConsultationRestriction>();

    // FEATURE 05 — política de bloqueo de preflight (soat/rtm/estado/fines/rnmc) por OT de la compañía.
    public DbSet<TenantTransitOfficeBlockingPolicy> TenantTransitOfficeBlockingPolicies =>
        Set<TenantTransitOfficeBlockingPolicy>();

    /// <summary>
    /// Opt-out de documento de prenda obligatorio por compañía + OT.
    /// Ausencia de fila = obligatorio; <c>document_optional=true</c> = opcional.
    /// </summary>
    public DbSet<TenantTransitOfficePrendaDocumentPolicy> TenantTransitOfficePrendaDocumentPolicies =>
        Set<TenantTransitOfficePrendaDocumentPolicy>();

    // ── Admin OT — mandatarios (firmantes de mandato) y sus compañías (ADR-0023) ──
    public DbSet<MandateSigner> MandateSigners => Set<MandateSigner>();

    public DbSet<MandateSignerCompany> MandateSignerCompanies => Set<MandateSignerCompany>();

    /// <summary>
    /// HU #11201 — organismos donde aplica cada mandatario. Fuente de verdad desde este cambio;
    /// <c>MandateSigner.TransitOfficeId</c> queda como organismo primario para compatibilidad.
    /// </summary>
    public DbSet<MandateSignerTransitOffice> MandateSignerTransitOffices =>
        Set<MandateSignerTransitOffice>();

    /// <summary>
    /// Empresas representadas para las que firma cada mandatario, por organismo. Distinto de
    /// <see cref="MandateSignerCompanies"/>, que lleva la compañía GESTORA.
    /// </summary>
    public DbSet<MandateSignerRepresentedCompany> MandateSignerRepresentedCompanies =>
        Set<MandateSignerRepresentedCompany>();

    // ── Admin OT — configuración de mandato por OT (ADR-0036, HU #10912) ───────────
    public DbSet<TransitOfficeMandateConfigEntity> TransitOfficeMandateConfigs =>
        Set<TransitOfficeMandateConfigEntity>();

    /// <summary>Tipo de mandato (3) por compañía gestora × OT.</summary>
    public DbSet<CompanyOtMandateRuleEntity> CompanyOtMandateRules =>
        Set<CompanyOtMandateRuleEntity>();

    // ── Admin Compañías — baúl de firmas precargadas (HU #10642, ADR-0025) ─────────
    public DbSet<SignatureVaultEntity> SignatureVault => Set<SignatureVaultEntity>();

    // ── Admin Compañías — representantes legales por compañía + escrituras (HU #10900, ADR-0033) ──
    public DbSet<RepresentedCompanyEntity> RepresentedCompanies => Set<RepresentedCompanyEntity>();

    public DbSet<CompanyLegalRepresentativeEntity> CompanyLegalRepresentatives =>
        Set<CompanyLegalRepresentativeEntity>();

    public DbSet<CompanyLegalRepresentativeProcedureTypeEntity> CompanyLegalRepresentativeProcedureTypes =>
        Set<CompanyLegalRepresentativeProcedureTypeEntity>();

    // Puente representante ↔ compañía (HU #10932, Feature #10929): representante multiempresa.
    public DbSet<LegalRepresentativeCompanyEntity> LegalRepresentativeCompanies =>
        Set<LegalRepresentativeCompanyEntity>();

    public DbSet<CompanyDeedEntity> CompanyDeeds => Set<CompanyDeedEntity>();

    public DbSet<CompanyDeedCompanyEntity> CompanyDeedCompanies => Set<CompanyDeedCompanyEntity>();

    // HU #11313 (Feature #11309, ADR-0042) — versiones de documento personalizado por compañía.
    public DbSet<CompanyPersonalizedDocumentEntity> CompanyPersonalizedDocuments => Set<CompanyPersonalizedDocumentEntity>();

    // ── Admin Compañías — validación de identidad administrativa desacoplada (HU #10907, ADR-0034) ──
    public DbSet<AdminIdentityValidationEntity> AdminIdentityValidations =>
        Set<AdminIdentityValidationEntity>();

    public DbSet<TransitOffice> TransitOffices => Set<TransitOffice>();

    public DbSet<VehicleColor> VehicleColors => Set<VehicleColor>();

    /// <summary>Catálogo global de tipos de servicio del vehículo (sección 18 del FUR, ADR-0019).</summary>
    public DbSet<VehicleServiceType> VehicleServiceTypes => Set<VehicleServiceType>();

    /// <summary>
    /// Catálogo global de causales de rechazo (SuperAdmin). Sustituye al motivo escrito a mano
    /// como dato agregable del reporte de motivos.
    /// </summary>
    public DbSet<RejectionReason> RejectionReasons => Set<RejectionReason>();

    /// <summary>Causales marcadas por el organismo en cada evento de rechazo.</summary>
    public DbSet<ProcedureInstanceRejectionReason> ProcedureInstanceRejectionReasons =>
        Set<ProcedureInstanceRejectionReason>();

    // ── Admin OT — perfil y feature flags (HU #10152 DDL, HU #10215 API) ───────
    public DbSet<TransitOfficeProfile> TransitOfficeProfiles => Set<TransitOfficeProfile>();

    public DbSet<OtFeatureFlagEntity> OtFeatureFlags => Set<OtFeatureFlagEntity>();

    // Preferencias de UI por usuario (base compartida: elegir columnas visibles en tablas).
    public DbSet<UserUiPreferenceEntity> UserUiPreferences => Set<UserUiPreferenceEntity>();

    // Consultas que cada usuario del organismo arma y guarda sobre los trámites.
    public DbSet<OtSavedQueryEntity> OtSavedQueries => Set<OtSavedQueryEntity>();

    // Lo mismo, del otro lado del trámite: las que guarda un usuario de la empresa gestora.
    public DbSet<CompanySavedQueryEntity> CompanySavedQueries => Set<CompanySavedQueryEntity>();

    // Las que guarda SuperAdmin en modo «todas las compañías»: sin tenant, compartidas entre todo
    // el equipo de SuperAdmin.
    public DbSet<SuperAdminSavedQueryEntity> SuperAdminSavedQueries => Set<SuperAdminSavedQueryEntity>();

    // HU #10545 — requisitos configurables por OT (RNMC, ruta de placa, validación de identidad).
    public DbSet<OtRequirementsEntity> OtRequirements => Set<OtRequirementsEntity>();

    // HU #10650 (Feature #10587) — inventario de preasignación de placa.
    public DbSet<PlateRangeEntity> PlateRanges => Set<PlateRangeEntity>();

    public DbSet<PlateRangeDetailEntity> PlateRangeDetails => Set<PlateRangeDetailEntity>();

    public DbSet<OtWebhookSubscriptionEntity> OtWebhookSubscriptions => Set<OtWebhookSubscriptionEntity>();

    public DbSet<OtApiCallLogEntity> OtApiCallLogs => Set<OtApiCallLogEntity>();

    // HU #10466 — historial de improntas generadas (ADR-0022). Sin RLS (dispensa documentada A10).
    public DbSet<ImprontaGenerationEntity> ImprontaGenerations => Set<ImprontaGenerationEntity>();

    public DbSet<OtDocumentPrecedenceEntity> OtDocumentPrecedences => Set<OtDocumentPrecedenceEntity>();

    public DbSet<OtDocumentTagEntity> OtDocumentTags => Set<OtDocumentTagEntity>();

    // ── Admin / parametrización documental (develop, HU #10193–#10198) ─────────
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<ProcedureDocumentRequirement> ProcedureDocumentRequirements => Set<ProcedureDocumentRequirement>();

    public DbSet<DocumentOrderOverride> DocumentOrderOverrides => Set<DocumentOrderOverride>();

    public DbSet<DocumentRequirementOverride> DocumentRequirementOverrides => Set<DocumentRequirementOverride>();

    // HU #10521 (RF31) — parámetros documentales por compañía gestora.
    public DbSet<Entities.Admin.CompanyDocumentParamEntity> CompanyDocumentParams => Set<Entities.Admin.CompanyDocumentParamEntity>();

    // Snapshot documental inmutable (HU #10197). Ancla a la instancia canónica del runtime.
    public DbSet<ProcedureDocumentSnapshot> ProcedureDocumentSnapshots => Set<ProcedureDocumentSnapshot>();

    // ── Trámites: catálogos globales del rework (#10128, ADR-0019: sin tenant_id) ─
    public DbSet<ProcedureType> ProcedureTypes => Set<ProcedureType>();
    public DbSet<ProcedureEntity> ProcedureEntities => Set<ProcedureEntity>();
    public DbSet<ConformationRule> ConformationRules => Set<ConformationRule>();
    public DbSet<ProcedureStep> ProcedureSteps => Set<ProcedureStep>();
    public DbSet<ProcedureSection> ProcedureSections => Set<ProcedureSection>();
    public DbSet<FormField> FormFields => Set<FormField>();
    public DbSet<ExternalDataSource> ExternalDataSources => Set<ExternalDataSource>();
    public DbSet<ConsultationTemplate> ConsultationTemplates => Set<ConsultationTemplate>();
    public DbSet<FieldApiBinding> FieldApiBindings => Set<FieldApiBinding>();

    // Trámites — runtime instancias (HU10150, con tenant_id)
    public DbSet<ProcedureInstance> ProcedureInstances => Set<ProcedureInstance>();
    public DbSet<ProcedureInstanceActor> ProcedureInstanceActors => Set<ProcedureInstanceActor>();
    public DbSet<ProcedureInstanceFieldValue> ProcedureInstanceFieldValues => Set<ProcedureInstanceFieldValue>();
    public DbSet<ProcedureInstanceStatusHistory> ProcedureInstanceStatusHistories => Set<ProcedureInstanceStatusHistory>();

    // Trámites — rework núcleo (Slice 1): attachments, preflight, comercial, eventos
    public DbSet<ProcedureInstanceAttachment> ProcedureInstanceAttachments => Set<ProcedureInstanceAttachment>();
    public DbSet<ProcedureInstancePreflightSnapshot> ProcedureInstancePreflightSnapshots => Set<ProcedureInstancePreflightSnapshot>();
    public DbSet<ProcedureInstanceCommercial> ProcedureInstanceCommercials => Set<ProcedureInstanceCommercial>();
    public DbSet<ProcedureInstanceEvent> ProcedureInstanceEvents => Set<ProcedureInstanceEvent>();

    // Trámites — biométrica (Slice 6, mock)
    public DbSet<ProcedureInstanceBiometricValidation> ProcedureInstanceBiometricValidations => Set<ProcedureInstanceBiometricValidation>();

    // Trámites — marcas de firma a posteriori (HU #11196): el lote que se firma cuando el representante valida
    public DbSet<DeferredSignatureMark> DeferredSignatureMarks => Set<DeferredSignatureMark>();

    // Trámites — outbox de eventos de validación de identidad (HU #10233, fase 2 event-driven)
    public DbSet<IdentityValidationOutbox> IdentityValidationOutbox => Set<IdentityValidationOutbox>();

    // Trámites — outbox de cambios de estado del trámite (N 03 RNF01, ADR-0022)
    public DbSet<ProcedureStateChangeOutbox> ProcedureStateChangeOutbox => Set<ProcedureStateChangeOutbox>();

    // Trámites — cola de despachos de correo al cambio de estado (HU #11461, ADR-0045)
    public DbSet<ProcedureStateChangeEmailDispatch> ProcedureStateChangeEmailDispatches =>
        Set<ProcedureStateChangeEmailDispatch>();

    // Trámites — cola de despachos de correo al asignar placa (HU #11484, ADR-0046)
    public DbSet<PlateAssignmentEmailDispatch> PlateAssignmentEmailDispatches =>
        Set<PlateAssignmentEmailDispatch>();

    // Trámites — bitácora ÚNICA del ciclo de validación de identidad (envío/webhook/descifrado/errores)
    public DbSet<IdentityValidationAuditEvent> IdentityValidationAudits => Set<IdentityValidationAuditEvent>();

    // Trámites — firma electrónica (Slice 7, mock)
    public DbSet<ProcedureInstanceSignature> ProcedureInstanceSignatures => Set<ProcedureInstanceSignature>();

    // Trámites — portal público de participantes (Slice 7 Part B)
    public DbSet<ProcedureInstanceParticipant> ProcedureInstanceParticipants => Set<ProcedureInstanceParticipant>();

    // Trámites — prenda / gravamen (IT-3, Feature #10585): agregado compañero con versionado por estado.
    public DbSet<ProcedureInstancePrenda> ProcedureInstancePrendas => Set<ProcedureInstancePrenda>();

    // Trámites — avalúo comercial (Feature #10707): valores de referencia por VIN/placa y fuente.
    public DbSet<AvaluoMockValue> AvaluoMockValues => Set<AvaluoMockValue>();

    // Trámites — caché cross-trámite de consultas externas + gate de consentimiento Habeas Data
    // (HU #10878, Feature #10862, CF-04, ADR-0030/ADR-0031).
    public DbSet<ExternalQueryCacheEntry> ExternalQueryCache => Set<ExternalQueryCacheEntry>();
    public DbSet<PersonDataConsent> PersonDataConsents => Set<PersonDataConsent>();

    // Trámites — certificaciones externas en modelo canónico persistido (HU #11302, Feature #11301,
    // ADR-0041). Almacén propio para SOAT, RTM y registro mercantil: reemplaza las llaves sueltas de
    // field_values, que son inmutables fuera de borrador y por tanto irreparables, y admite el
    // histórico que el RUNT ya entrega. El payload crudo habilita reprocesar un mapeo corregido sin
    // volver a pagar la consulta.
    public DbSet<ExternalQueryPayload> ExternalQueryPayloads => Set<ExternalQueryPayload>();
    public DbSet<VehicleSoatPolicy> VehicleSoatPolicies => Set<VehicleSoatPolicy>();
    public DbSet<VehicleRtmInspection> VehicleRtmInspections => Set<VehicleRtmInspection>();
    public DbSet<CompanyRegistration> CompanyRegistrations => Set<CompanyRegistration>();

    // Trámites — entidad persona/sujeto a nivel tenant para prevalidaciones de identidad
    // (HU #10865, Feature #10864, CF-00, ADR-0030).
    public DbSet<Person> Persons => Set<Person>();

    // FEATURE-08 / Fase 2b — catálogo global de fuentes por tipo (CFD-04, ADR-0019 excepción A4/A20).
    public DbSet<ProcedureTypeSource> ProcedureTypeSources => Set<ProcedureTypeSource>();

    // FEATURE-08 / Fase 2b — snapshots inmutables del tipo al crear instancia (CFD-01/AC#5).
    public DbSet<ProcedureTypeSnapshot> ProcedureTypeSnapshots => Set<ProcedureTypeSnapshot>();

    // Quipux — radicación de trámites en secretarías de tránsito. El activador es el modo QX del
    // perfil OT (admin.transit_office_profiles.operation_mode = 'quipux').
    public DbSet<QuipuxSubmission> QuipuxSubmissions => Set<QuipuxSubmission>();

    // Quipux — bitácora por radicación (trazabilidad de la ejecución a la finalización).
    public DbSet<QuipuxSubmissionEvent> QuipuxSubmissionEvents => Set<QuipuxSubmissionEvent>();

    // Quipux — bitácora por ejecución del worker (¿corrió el cron? ¿cuántos falló?).
    public DbSet<QuipuxJobRun> QuipuxJobRuns => Set<QuipuxJobRun>();

    // Quipux — configuración operativa (fila única, secretos cifrados). Entidad de persistencia:
    // el dominio los ve en claro, la BD solo cifrados.
    internal DbSet<QuipuxSettingsRow> QuipuxSettings => Set<QuipuxSettingsRow>();

    // Banco de pruebas de notificaciones (HU #11365) — fila única y global de plataforma: buzón de
    // pruebas y marca del último envío. Sin tenant_id (y por tanto sin RLS) a propósito: lo gestiona
    // el SuperAdmin, no un cliente.
    internal DbSet<Entities.Admin.NotificationTestSettingsRow> NotificationTestSettings =>
        Set<Entities.Admin.NotificationTestSettingsRow>();

    // Bitácora append-only de intentos de envío de notificación (HU #11363, esquema de la
    // HU #11357). tenant_id NOT NULL en BD — ver NotificationDeliveryLoggingEmailSender.
    internal DbSet<Entities.Admin.NotificationDeliveryLogEntity> NotificationDeliveryLogs =>
        Set<Entities.Admin.NotificationDeliveryLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlitDbContext).Assembly);
    }
}
