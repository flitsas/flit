using Flit.Diagnostico.Api.Modulos.Catalogos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.organismos</c> (HU #13723, plan §5, patrón Comparendos HU13459): réplica global de organismos de tránsito con
/// su departamento. Catálogo de FLIT sin <c>tenant_id</c> ni filtro por empresa (excepción por tabla de ADR-0072); la clave es
/// el código del organismo.
/// </summary>
internal sealed class OrganismoConfig : IEntityTypeConfiguration<Organismo>
{
    public void Configure(EntityTypeBuilder<Organismo> builder)
    {
        builder.ToTable("organismos", t => t.HasComment("Réplica global de organismos de tránsito con su departamento (HU #13723). Sin tenant."));
        builder.HasKey(x => x.Codigo).HasName("pk_organismos");
        builder.Property(x => x.Codigo).HasMaxLength(16).ValueGeneratedNever().HasComment("Código del organismo de tránsito.");
        builder.Property(x => x.Nombre).HasMaxLength(200).HasComment("Nombre del organismo.");
        builder.Property(x => x.NombreNorm).HasMaxLength(200).HasComment("Nombre normalizado para buscar por el transit_office_name del RUNT.");
        builder.Property(x => x.DepartamentoCodigo).HasMaxLength(8).HasComment("Código del departamento (DIVIPOLA).");
        builder.Property(x => x.DepartamentoNombre).HasMaxLength(100).HasComment("Nombre del departamento.");
        builder.HasIndex(x => x.NombreNorm).HasDatabaseName("ix_organismos_nombre_norm");
    }
}
