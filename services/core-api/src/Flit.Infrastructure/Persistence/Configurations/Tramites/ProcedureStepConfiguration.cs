using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureStepConfiguration : IEntityTypeConfiguration<ProcedureStep>
{
    public void Configure(EntityTypeBuilder<ProcedureStep> builder)
    {
        builder.ToTable("procedure_steps", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(150).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.ProcedureTypeId).HasDatabaseName("ix_procedure_steps_procedure_type_id");

        builder.HasMany(x => x.Sections)
            .WithOne(x => x.ProcedureStep)
            .HasForeignKey(x => x.ProcedureStepId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
