using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence;

/// <summary>
/// DbContext del servicio core-ict (schema <c>ict</c>). Las tablas se crean con DDL crudo embebido
/// (RLS, triggers, funciones) y se mapean con <c>ExcludeFromMigrations</c>: EF no gestiona su DDL,
/// solo las consulta. El bootstrap del schema corre al arrancar (IctSchemaBootstrapper).
/// </summary>
public sealed class IctDbContext(DbContextOptions<IctDbContext> options) : DbContext(options)
{
    public DbSet<IntegrationClient> IntegrationClients => Set<IntegrationClient>();

    public DbSet<ProcedureTypeMapping> ProcedureTypeMappings => Set<ProcedureTypeMapping>();

    public DbSet<ExternalIntegrationMaster> Masters => Set<ExternalIntegrationMaster>();

    public DbSet<ExternalIntegrationActor> Actors => Set<ExternalIntegrationActor>();

    public DbSet<TransactionAttachment> Attachments => Set<TransactionAttachment>();

    public DbSet<AllowedDocument> AllowedDocuments => Set<AllowedDocument>();

    public DbSet<ConfigurationDocument> ConfigurationDocuments => Set<ConfigurationDocument>();

    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaNames.Ict);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IctDbContext).Assembly);
    }
}
