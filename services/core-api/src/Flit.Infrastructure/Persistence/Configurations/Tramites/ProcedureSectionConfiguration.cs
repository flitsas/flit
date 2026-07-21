using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureSectionConfiguration : IEntityTypeConfiguration<ProcedureSection>
{
    public void Configure(EntityTypeBuilder<ProcedureSection> builder)
    {
        builder.ToTable("procedure_sections", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Layout).HasMaxLength(20).IsRequired().HasDefaultValue("single");

        // F08 Fase 2b: tipo de renderer para SectionRendererRegistry (CFD-09)
        builder.Property(x => x.SectionType).HasMaxLength(40).IsRequired().HasDefaultValue("generic_form");

        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.ProcedureStepId).HasDatabaseName("ix_procedure_sections_procedure_step_id");

        builder.HasMany(x => x.FormFields)
            .WithOne(x => x.ProcedureSection)
            .HasForeignKey(x => x.ProcedureSectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
