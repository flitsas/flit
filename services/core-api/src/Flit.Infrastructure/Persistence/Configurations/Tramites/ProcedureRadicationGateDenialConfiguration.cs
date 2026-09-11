using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureRadicationGateDenialConfiguration
    : IEntityTypeConfiguration<ProcedureRadicationGateDenial>
{
    public void Configure(EntityTypeBuilder<ProcedureRadicationGateDenial> builder)
    {
        builder.ToTable("procedure_radication_gate_denials", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DenialReason).HasMaxLength(80).IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.OccurredAt })
            .HasDatabaseName("ix_procedure_radication_gate_denials_tenant_occurred");
    }
}
