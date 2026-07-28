using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class MandateSignerConfiguration : IEntityTypeConfiguration<MandateSigner>
{
    public void Configure(EntityTypeBuilder<MandateSigner> builder)
    {
        builder.ToTable("mandate_signers", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(10).IsRequired().HasDefaultValue("CC");
        builder.Property(x => x.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IntegrityHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.SignatureVaultId);
        builder.Property(x => x.IdentityValidationRef);
        builder.Property(x => x.UserId);
        builder.Property(x => x.RegisteredAt).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();

        // Consulta principal: mandatarios (activos) por OT.
        builder.HasIndex(x => new { x.TransitOfficeId, x.IsActive })
            .HasDatabaseName("ix_mandate_signers_transit_office_id_is_active");

        // ADR-0036 §D9 — cotejo del firmante por cuenta de usuario al aprobar.
        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_mandate_signers_user_id");
        // Índice de la FK al baúl (checklist A9).
        builder.HasIndex(x => x.SignatureVaultId)
            .HasDatabaseName("ix_mandate_signers_signature_vault_id");
    }
}
