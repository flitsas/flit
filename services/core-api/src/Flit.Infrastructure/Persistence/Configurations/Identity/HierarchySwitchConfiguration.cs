using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Identity;

/// <summary>
/// HU #12323 — interruptores globales de la jerarquía. Sin <c>tenant_id</c> ni RLS (configuración de
/// plataforma). El CHECK de claves y el seed viven solo en SQL
/// (108-HU12323-hierarchy-switches-and-link-audit.sql).
/// </summary>
internal sealed class HierarchySwitchConfiguration : IEntityTypeConfiguration<HierarchySwitch>
{
    public void Configure(EntityTypeBuilder<HierarchySwitch> builder)
    {
        builder.ToTable("hierarchy_switches", SchemaNames.Identity, t =>
        {
            t.HasTrigger("tr_hierarchy_switches_row_version");
            t.HasTrigger("tr_hierarchy_switches_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_hierarchy_switches");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.SwitchKey).HasColumnName("switch_key").HasColumnType("text").IsRequired();
        builder.HasIndex(x => x.SwitchKey).IsUnique().HasDatabaseName("uq_hierarchy_switches_switch_key");

        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
    }
}
