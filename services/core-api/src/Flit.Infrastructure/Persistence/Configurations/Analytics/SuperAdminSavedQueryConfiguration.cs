using Flit.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>
/// Mapeo EF Core de las consultas guardadas de SuperAdmin. El DDL lo lleva la migración SQL cruda
/// (<c>72-superadmin-consultas-guardadas.sql</c>), igual que <c>CompanySavedQueryConfiguration</c>,
/// porque incluye un índice único sobre una expresión que el generador de EF no sabe expresar.
/// </summary>
internal sealed class SuperAdminSavedQueryConfiguration : IEntityTypeConfiguration<SuperAdminSavedQueryEntity>
{
    public void Configure(EntityTypeBuilder<SuperAdminSavedQueryEntity> builder)
    {
        builder.ToTable("superadmin_saved_queries", "analytics", t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Descripcion).HasMaxLength(400);
        builder.Property(x => x.Definicion).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.Nombre).HasDatabaseName("ix_superadmin_saved_queries_nombre");
    }
}
