using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Security;

internal sealed class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.ToTable("user_invitations", SchemaNames.Security);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired(false);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RoleId).IsRequired(false);
        builder.Property(x => x.LastSentAt).IsRequired(false);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
    }
}

/// <summary>HU #10506 AC4: tabla puente N:M invitación-roles (tenant-scoped, RLS estándar).</summary>
internal sealed class InvitationRoleConfiguration : IEntityTypeConfiguration<InvitationRole>
{
    public void Configure(EntityTypeBuilder<InvitationRole> builder)
    {
        builder.ToTable("invitation_roles", SchemaNames.Security);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.HasOne(x => x.Invitation).WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        // tenant_id primero (checklist A11); unicidad de negocio (invitation_id, role_id).
        builder.HasIndex(x => new { x.TenantId, x.InvitationId, x.RoleId }).IsUnique();
    }
}
