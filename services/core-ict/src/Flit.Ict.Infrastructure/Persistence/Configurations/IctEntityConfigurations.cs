using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Ict.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuraciones EF de las entidades del schema <c>ict</c>. Las tablas las crea el DDL crudo
/// embebido (IctSchemaBootstrapper); aquí solo se mapean nombre de tabla, clave, concurrencia
/// (row_version, mantenido por trigger) y tipos de columna. Con <c>ExcludeFromMigrations</c> EF no
/// gestiona su DDL.
/// </summary>
internal sealed class IntegrationClientConfiguration : IEntityTypeConfiguration<IntegrationClient>
{
    public void Configure(EntityTypeBuilder<IntegrationClient> builder)
    {
        builder.ToTable("integration_clients", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Username).HasColumnType("citext");
        builder.Property(x => x.Scopes).HasColumnType("jsonb");
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);
        builder.HasIndex(x => x.Username).IsUnique();
    }
}

internal sealed class ProcedureTypeMappingConfiguration : IEntityTypeConfiguration<ProcedureTypeMapping>
{
    public void Configure(EntityTypeBuilder<ProcedureTypeMapping> builder)
    {
        builder.ToTable("procedure_type_mapping", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);
        builder.HasIndex(x => x.ExternalTransactionType).IsUnique();
    }
}

internal sealed class ExternalIntegrationMasterConfiguration : IEntityTypeConfiguration<ExternalIntegrationMaster>
{
    public void Configure(EntityTypeBuilder<ExternalIntegrationMaster> builder)
    {
        builder.ToTable("external_integration_master", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SellingPrice).HasColumnType("numeric(19,2)");
        builder.Property(x => x.BusinessCommentsValidation).HasColumnType("text");
        builder.Property(x => x.ExternalCommentsValidation).HasColumnType("text");
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);
        builder.HasMany(x => x.Actors)
            .WithOne()
            .HasForeignKey(a => a.MasterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.ManagerIdTransaction });
    }
}

internal sealed class ExternalIntegrationActorConfiguration : IEntityTypeConfiguration<ExternalIntegrationActor>
{
    public void Configure(EntityTypeBuilder<ExternalIntegrationActor> builder)
    {
        builder.ToTable("external_integration_actors", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);
        builder.HasIndex(x => x.MasterId);
    }
}
