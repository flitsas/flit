using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de solo lectura de <see cref="ProcedureType"/> a <c>tramites.procedure_types</c>
/// (DDL HU #10151). Solo se mapean las columnas que consume la HU #10195; los nombres
/// se derivan por la convención snake_case global.
/// </summary>
internal sealed class ProcedureTypeConfiguration : IEntityTypeConfiguration<ProcedureType>
{
    public void Configure(EntityTypeBuilder<ProcedureType> builder)
    {
        builder.ToTable("procedure_types", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
    }
}
