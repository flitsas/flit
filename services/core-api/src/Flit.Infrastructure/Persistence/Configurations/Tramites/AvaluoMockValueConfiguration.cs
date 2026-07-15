using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class AvaluoMockValueConfiguration : IEntityTypeConfiguration<AvaluoMockValue>
{
    public void Configure(EntityTypeBuilder<AvaluoMockValue> builder)
    {
        builder.ToTable("avaluo_mock_values", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.MatchKey).HasColumnName("match_key").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(30).IsRequired();
        builder.Property(x => x.ValueCop).HasColumnName("value_cop").HasColumnType("numeric(15,2)").IsRequired();

        builder.HasIndex(x => new { x.MatchKey, x.Source })
            .IsUnique()
            .HasDatabaseName("uq_avaluo_mock");
    }
}
