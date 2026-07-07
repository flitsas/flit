using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceActorConfiguration : IEntityTypeConfiguration<ProcedureInstanceActor>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceActor> builder)
    {
        builder.ToTable("procedure_instance_actors", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_actors_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ActorType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DocumentNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.Phone).HasMaxLength(20);
        builder.Property(x => x.PersonType).HasColumnName("person_type").HasMaxLength(10);
        builder.Property(x => x.EsRepresentanteLegal)
            .HasColumnName("es_representante_legal")
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(x => x.Metadata).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.ProcedureInstanceId, x.ProcedureEntityId })
            .IsUnique()
            .HasDatabaseName("uq_procedure_instance_actors_instance_entity");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_procedure_instance_actors_tenant_id");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.Actors)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_actors_procedure_instances");

        builder.HasOne(x => x.ProcedureEntity)
            .WithMany()
            .HasForeignKey(x => x.ProcedureEntityId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_procedure_instance_actors_procedure_entities");
    }
}
