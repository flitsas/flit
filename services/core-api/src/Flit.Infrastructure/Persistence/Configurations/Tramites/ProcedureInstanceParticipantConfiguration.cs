using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceParticipantConfiguration
    : IEntityTypeConfiguration<ProcedureInstanceParticipant>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceParticipant> builder)
    {
        builder.ToTable("procedure_instance_participants", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_participants_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Rol).HasColumnName("rol").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(x => x.Telefono).HasColumnName("telefono").HasMaxLength(40);
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.WhatsappOptIn).HasColumnName("whatsapp_opt_in").IsRequired().HasDefaultValue(false);
        builder.Property(x => x.Consent1581At).HasColumnName("consent_1581_at");
        builder.Property(x => x.ConsentVersion).HasColumnName("consent_version").HasMaxLength(40);
        builder.Property(x => x.ConsentIp).HasColumnName("consent_ip").HasMaxLength(64);
        builder.Property(x => x.ConsentUserAgent).HasColumnName("consent_user_agent").HasMaxLength(120);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.LastReminderAt).HasColumnName("last_reminder_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_procedure_instance_participants_tenant_id_instance");

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("uq_procedure_instance_participants_token_hash");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.Participants)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_participants_procedure_instances");
    }
}
