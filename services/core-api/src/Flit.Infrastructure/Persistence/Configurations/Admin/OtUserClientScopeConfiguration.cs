using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class OtUserClientScopeConfiguration : IEntityTypeConfiguration<OtUserClientScopeEntity>
{
    public void Configure(EntityTypeBuilder<OtUserClientScopeEntity> builder)
    {
        builder.ToTable("ot_user_client_scopes", SchemaNames.Admin);

        builder.HasKey(x => x.Id).HasName("pk_ot_user_client_scopes");
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.Mode).IsRequired().HasMaxLength(12).HasDefaultValue(OtUserClientScopeModes.Todos);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now()");
        // row_version lo incrementa el trigger trg_row_version; EF no lo escribe.
        builder.Property(x => x.RowVersion).IsRequired().HasDefaultValue(0L).ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(x => new { x.UserId, x.TransitOfficeId }).IsUnique().HasDatabaseName("uq_ot_user_client_scopes_user_office");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_ot_user_client_scopes_tenant_id");
        builder.HasIndex(x => x.TransitOfficeId).HasDatabaseName("ix_ot_user_client_scopes_transit_office_id");

        // Relación EF (la FK ya existe en el DDL 197): sin ella EF no sabe ordenar el INSERT del alcance después del alta del usuario
        // cuando ambos se guardan en el mismo SaveChanges (activación de una cuenta invitada con alcance).
        builder.HasOne<Flit.Infrastructure.Persistence.Entities.Identity.User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_ot_user_client_scopes_users")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Clients)
            .WithOne(c => c.Scope)
            .HasForeignKey(c => c.ScopeId)
            .HasConstraintName("fk_ot_user_client_scope_clients_scopes")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OtUserClientScopeClientConfiguration : IEntityTypeConfiguration<OtUserClientScopeClientEntity>
{
    public void Configure(EntityTypeBuilder<OtUserClientScopeClientEntity> builder)
    {
        builder.ToTable("ot_user_client_scope_clients", SchemaNames.Admin);

        builder.HasKey(x => new { x.ScopeId, x.ClientTenantId }).HasName("pk_ot_user_client_scope_clients");
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasIndex(x => x.ClientTenantId).HasDatabaseName("ix_ot_user_client_scope_clients_client_tenant_id");
    }
}

internal sealed class OtInvitationClientScopeClientConfiguration : IEntityTypeConfiguration<OtInvitationClientScopeClientEntity>
{
    public void Configure(EntityTypeBuilder<OtInvitationClientScopeClientEntity> builder)
    {
        builder.ToTable("ot_invitation_client_scope_clients", SchemaNames.Admin);

        builder.HasKey(x => new { x.InvitationId, x.ClientTenantId }).HasName("pk_ot_invitation_client_scope_clients");
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasIndex(x => x.ClientTenantId).HasDatabaseName("ix_ot_invitation_client_scope_clients_client_tenant_id");
    }
}
