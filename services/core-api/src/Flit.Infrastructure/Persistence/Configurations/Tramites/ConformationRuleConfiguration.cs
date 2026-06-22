using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ConformationRuleConfiguration : IEntityTypeConfiguration<ConformationRule>
{
    public void Configure(EntityTypeBuilder<ConformationRule> builder)
    {
        builder.ToTable("conformation_rules", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.HasIndex(x => new { x.ProcedureTypeId, x.ProcedureEntityId })
            .IsUnique()
            .HasDatabaseName("uq_conformation_rules_type_entity");

        builder.Property(x => x.ValidationProfile).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne(x => x.ProcedureEntity)
            .WithMany()
            .HasForeignKey(x => x.ProcedureEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
