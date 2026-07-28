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

internal sealed class PretramiteEventConfiguration : IEntityTypeConfiguration<PretramiteEvent>
{
    public void Configure(EntityTypeBuilder<PretramiteEvent> builder)
    {
        builder.ToTable("pretramite_events", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        // uuidv7 lo genera la BD (id time-ordered): no usar Guid.NewGuid client-side.
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.Detail).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.MasterId, x.CreatedAt });
    }
}
