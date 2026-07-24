using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ExternalDataSourceConfiguration : IEntityTypeConfiguration<ExternalDataSource>
{
    public void Configure(EntityTypeBuilder<ExternalDataSource> builder)
    {
        builder.ToTable("external_data_sources", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_external_data_sources_code");

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.AuthType).HasMaxLength(20).IsRequired().HasDefaultValue("none");
        builder.Property(x => x.ExternalRefs).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");

        // HU #10878 (ADR-0030) — TTL de caché cross-trámite GLOBAL por fuente (columna nueva
        // agregada por migración SQL cruda, sin tabla nueva; excepción A20 ya aplicada a este catálogo).
        builder.Property(x => x.CacheTtlHours).HasColumnName("cache_ttl_hours");

        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
