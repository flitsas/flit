using Flit.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>
/// Mapeo EF Core de las consultas guardadas de ICT. El DDL lo lleva la migración SQL cruda
/// (<c>77-ict-consultas-guardadas.sql</c>) porque incluye un índice único sobre una expresión —el
/// nombre normalizado— que el generador de EF no sabe expresar. Entidad <c>ExcludeFromMigrations</c>
/// para que el snapshot refleje el modelo sin que EF intente generar el DDL por su cuenta.
/// </summary>
internal sealed class IctSavedQueryConfiguration : IEntityTypeConfiguration<IctSavedQueryEntity>
{
    public void Configure(EntityTypeBuilder<IctSavedQueryEntity> builder)
    {
        builder.ToTable("ict_saved_queries", "analytics", t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Descripcion).HasMaxLength(400);
        builder.Property(x => x.Definicion).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Toda lectura filtra por empresa + usuario y ordena por nombre.
        builder.HasIndex(x => new { x.TenantId, x.UserId })
            .HasDatabaseName("ix_ict_saved_queries_tenant_user");
    }
}
