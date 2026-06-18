using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureTypeConfiguration : IEntityTypeConfiguration<ProcedureType>
{
    public void Configure(EntityTypeBuilder<ProcedureType> builder)
    {
        builder.ToTable("procedure_types", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_procedure_types_code");

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Family).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.Property(x => x.PublicationStatus).HasMaxLength(20).IsRequired().HasDefaultValue("draft");
        builder.Property(x => x.ExternalRefs).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");

        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasMany(x => x.ConformationRules)
            .WithOne(x => x.ProcedureType)
            .HasForeignKey(x => x.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Steps)
            .WithOne(x => x.ProcedureType)
            .HasForeignKey(x => x.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
