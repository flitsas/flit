using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
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

    // Tramites — catálogos globales (ADR-0019: sin tenant_id)
    public DbSet<ProcedureType> ProcedureTypes => Set<ProcedureType>();
    public DbSet<ProcedureEntity> ProcedureEntities => Set<ProcedureEntity>();
    public DbSet<ConformationRule> ConformationRules => Set<ConformationRule>();
    public DbSet<ProcedureStep> ProcedureSteps => Set<ProcedureStep>();
    public DbSet<ProcedureSection> ProcedureSections => Set<ProcedureSection>();
    public DbSet<FormField> FormFields => Set<FormField>();
    public DbSet<ExternalDataSource> ExternalDataSources => Set<ExternalDataSource>();
    public DbSet<ConsultationTemplate> ConsultationTemplates => Set<ConsultationTemplate>();
    public DbSet<FieldApiBinding> FieldApiBindings => Set<FieldApiBinding>();

    // Tramites — runtime instancias (HU10150, con tenant_id)
    public DbSet<ProcedureInstance> ProcedureInstances => Set<ProcedureInstance>();
    public DbSet<ProcedureInstanceActor> ProcedureInstanceActors => Set<ProcedureInstanceActor>();
    public DbSet<ProcedureInstanceFieldValue> ProcedureInstanceFieldValues => Set<ProcedureInstanceFieldValue>();
    public DbSet<ProcedureInstanceStatusHistory> ProcedureInstanceStatusHistories => Set<ProcedureInstanceStatusHistory>();

    // Tramites — rework núcleo (Slice 1): attachments, preflight, comercial, eventos
    public DbSet<ProcedureInstanceAttachment> ProcedureInstanceAttachments => Set<ProcedureInstanceAttachment>();
    public DbSet<ProcedureInstancePreflightSnapshot> ProcedureInstancePreflightSnapshots => Set<ProcedureInstancePreflightSnapshot>();
    public DbSet<ProcedureInstanceCommercial> ProcedureInstanceCommercials => Set<ProcedureInstanceCommercial>();
    public DbSet<ProcedureInstanceEvent> ProcedureInstanceEvents => Set<ProcedureInstanceEvent>();

    // Tramites — biométrica (Slice 6, mock)
    public DbSet<ProcedureInstanceBiometricValidation> ProcedureInstanceBiometricValidations => Set<ProcedureInstanceBiometricValidation>();

    // Tramites — firma electrónica (Slice 7, mock)
    public DbSet<ProcedureInstanceSignature> ProcedureInstanceSignatures => Set<ProcedureInstanceSignature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlitDbContext).Assembly);
    }
}
