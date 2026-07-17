using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceCommercialConfiguration : IEntityTypeConfiguration<ProcedureInstanceCommercial>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceCommercial> builder)
    {
        builder.ToTable("procedure_instance_commercial", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_commercial_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ValorVenta).HasColumnName("valor_venta").HasColumnType("numeric(18,2)");
        builder.Property(x => x.Causal).HasColumnName("causal").HasMaxLength(30);
        builder.Property(x => x.TasaImpuesto).HasColumnName("tasa_impuesto").HasColumnType("numeric(7,4)");
        builder.Property(x => x.Derechos).HasColumnName("derechos").HasColumnType("numeric(18,2)");
        builder.Property(x => x.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(40);

        // Feature #10707 — trazabilidad del avalúo sugerido.
        builder.Property(x => x.SuggestedValue).HasColumnName("suggested_value").HasColumnType("numeric(15,2)");
        builder.Property(x => x.SuggestedSource).HasColumnName("suggested_source").HasMaxLength(30);
        builder.Property(x => x.ValueOrigin).HasColumnName("value_origin").HasMaxLength(20);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.ProcedureInstanceId)
            .IsUnique()
            .HasDatabaseName("uq_procedure_instance_commercial_instance");

        builder.HasOne(x => x.ProcedureInstance)
            .WithOne(x => x.Commercial)
            .HasForeignKey<ProcedureInstanceCommercial>(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_commercial_procedure_instances");
    }
}
