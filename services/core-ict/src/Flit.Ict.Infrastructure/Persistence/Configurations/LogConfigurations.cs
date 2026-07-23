using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Ict.Infrastructure.Persistence.Configurations;

internal sealed class IntegrationLogConfiguration : IEntityTypeConfiguration<IntegrationLog>
{
    public void Configure(EntityTypeBuilder<IntegrationLog> builder)
    {
        builder.ToTable("integration_log", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Request).HasColumnType("jsonb");
        builder.Property(x => x.Response).HasColumnType("jsonb");
        builder.Property(x => x.Headers).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}
