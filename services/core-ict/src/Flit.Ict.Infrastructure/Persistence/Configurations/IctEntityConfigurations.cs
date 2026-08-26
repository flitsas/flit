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
        // username es varchar (NO citext): la case-insensitivity se resuelve normalizando a minúsculas en la
        // app + índice único funcional lower(username) (ver 01-ICT-schema-core.sql). Evita la dependencia de
        // la extensión citext, que no se puede crear con el rol de la app en PDN.
        builder.Property(x => x.Username).HasMaxLength(50);
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
        // Identificador numérico secuencial (paridad v1): lo genera la secuencia en la BD (DDL embebido);
        // EF no lo envía en el INSERT y lo lee de vuelta (RETURNING) para exponerlo en /register.
        builder.Property(x => x.TransactionNumber)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("nextval('ict.transaction_number_seq')");
        builder.Property(x => x.SellingPrice).HasColumnType("numeric(19,2)");
        builder.Property(x => x.BusinessCommentsValidation).HasColumnType("text");
        builder.Property(x => x.ExternalCommentsValidation).HasColumnType("text");
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);
        builder.HasMany(x => x.Actors)
            .WithOne()
            .HasForeignKey(a => a.MasterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Transformations)
            .WithOne()
            .HasForeignKey(t => t.MasterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.ManagerIdTransaction });
    }
}

internal sealed class ExternalIntegrationMasterTransformationConfiguration
    : IEntityTypeConfiguration<ExternalIntegrationMasterTransformation>
{
    public void Configure(EntityTypeBuilder<ExternalIntegrationMasterTransformation> builder)
    {
        builder.ToTable("external_integration_master_transformation_type", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        // PK compuesta (master, código RUNT): una transformación de cada tipo por pre-trámite.
        builder.HasKey(x => new { x.MasterId, x.IdTransformationType });
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
