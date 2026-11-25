using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>Réplica diaria de <c>CatalogosService.ListarOrganismosTransito</c> (catálogo global de FLIT, sin empresa).</summary>
public sealed class Organismo
{
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public string NombreNormalizado { get; set; } = string.Empty;

    public string? DepartamentoCodigo { get; set; }

    public string? Departamento { get; set; }

    public string? Municipio { get; set; }

    public DateTimeOffset SincronizadoEn { get; set; }
}

/// <summary><c>impuestos.organismos</c> (plan §4.1).</summary>
internal sealed class OrganismoConfig : IEntityTypeConfiguration<Organismo>
{
    public void Configure(EntityTypeBuilder<Organismo> builder)
    {
        builder.ToTable("organismos", t => t.HasComment("Réplica diaria de los organismos de tránsito (catálogo global, sin empresa)."));
        builder.HasKey(x => x.Codigo).HasName("pk_organismos");
        builder.Property(x => x.Codigo).HasMaxLength(16).ValueGeneratedNever();
        builder.Property(x => x.Nombre).HasMaxLength(200);
        builder.Property(x => x.NombreNormalizado).HasMaxLength(200);
        builder.Property(x => x.DepartamentoCodigo).HasMaxLength(2);
        builder.Property(x => x.Departamento).HasMaxLength(80);
        builder.Property(x => x.Municipio).HasMaxLength(80);
        builder.HasIndex(x => x.NombreNormalizado).HasDatabaseName("ix_org_nombre");
    }
}
