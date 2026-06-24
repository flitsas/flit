using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence;

public sealed class FlitDbContext(DbContextOptions<FlitDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

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

    public DbSet<TenantOperationalPolicy> TenantOperationalPolicies => Set<TenantOperationalPolicy>();

    public DbSet<TenantConfigAuditLog> TenantConfigAuditLogs => Set<TenantConfigAuditLog>();

    public DbSet<TenantWhitelistUser> TenantWhitelistUsers => Set<TenantWhitelistUser>();

    public DbSet<TenantTransitOfficeGrant> TenantTransitOfficeGrants => Set<TenantTransitOfficeGrant>();

    // ── Admin / parametrización documental (develop, HU #10193–#10198) ─────────
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<ProcedureDocumentRequirement> ProcedureDocumentRequirements => Set<ProcedureDocumentRequirement>();

    public DbSet<DocumentOrderOverride> DocumentOrderOverrides => Set<DocumentOrderOverride>();

    public DbSet<DocumentRequirementOverride> DocumentRequirementOverrides => Set<DocumentRequirementOverride>();

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

    // Trámites — outbox de eventos de validación de identidad (HU #10233, fase 2 event-driven)
    public DbSet<IdentityValidationOutbox> IdentityValidationOutbox => Set<IdentityValidationOutbox>();

    // Trámites — firma electrónica (Slice 7, mock)
    public DbSet<ProcedureInstanceSignature> ProcedureInstanceSignatures => Set<ProcedureInstanceSignature>();

    // Trámites — portal público de participantes (Slice 7 Part B)
    public DbSet<ProcedureInstanceParticipant> ProcedureInstanceParticipants => Set<ProcedureInstanceParticipant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlitDbContext).Assembly);
    }
}
