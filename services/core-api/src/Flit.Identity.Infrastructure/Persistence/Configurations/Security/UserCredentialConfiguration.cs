using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Security;

internal sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("user_credentials", SchemaNames.Security);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.UserId).IsRequired();
        builder.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("uq_user_credentials_user_id");

        builder.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(x => x.MustChangePassword).HasDefaultValue(false);
        builder.Property(x => x.FailedLoginAttempts).HasDefaultValue((short)0);

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_user_credentials_users")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
