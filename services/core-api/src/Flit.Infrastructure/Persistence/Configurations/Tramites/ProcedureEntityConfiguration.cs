using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureEntityConfiguration : IEntityTypeConfiguration<ProcedureEntity>
{
    public void Configure(EntityTypeBuilder<ProcedureEntity> builder)
    {
        builder.ToTable("procedure_entities", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_procedure_entities_code");

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalRefs).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
