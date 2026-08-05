using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de las consultas guardadas del organismo. El DDL lo lleva la migración SQL cruda
/// (<c>57-ot-consultas-guardadas.sql</c>), como el resto de tablas de <c>admin.*</c>: índices,
/// unicidad, RLS y triggers. Entidad <c>ExcludeFromMigrations</c> para que el snapshot refleje el
/// modelo sin que EF intente generar el DDL por su cuenta.
/// </summary>
internal sealed class OtSavedQueryConfiguration : IEntityTypeConfiguration<OtSavedQueryEntity>
{
    public void Configure(EntityTypeBuilder<OtSavedQueryEntity> builder)
    {
        builder.ToTable("ot_saved_queries", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_ot_saved_queries_row_version");
            t.HasTrigger("tr_ot_saved_queries_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Descripcion).HasMaxLength(400);
        builder.Property(x => x.Definicion).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Toda lectura filtra por organismo + usuario y ordena por nombre.
        builder.HasIndex(x => new { x.TransitOfficeId, x.UserId })
            .HasDatabaseName("ix_ot_saved_queries_office_user");
    }
}
