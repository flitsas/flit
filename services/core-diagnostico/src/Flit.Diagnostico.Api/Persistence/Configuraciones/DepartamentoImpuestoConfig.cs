using Flit.Diagnostico.Api.Modulos.Catalogos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.departamentos_impuesto</c> (HU #13723, plan §5, R-9, R-23): caché global del rpc de capacidad de
/// core-consultas, sin <c>tenant_id</c> (excepción por tabla de ADR-0072) y sin edición manual.
/// </summary>
internal sealed class DepartamentoImpuestoConfig : IEntityTypeConfiguration<DepartamentoImpuesto>
{
    public void Configure(EntityTypeBuilder<DepartamentoImpuesto> builder)
    {
        builder.ToTable("departamentos_impuesto", t => t.HasComment("Departamentos con impuesto vehicular soportado por core-consultas (R-9, HU #13723). Sin tenant."));
        builder.HasKey(x => x.DepartamentoCodigo).HasName("pk_departamentos_impuesto");
        builder.Property(x => x.DepartamentoCodigo).HasMaxLength(8).ValueGeneratedNever().HasComment("Código del departamento (DIVIPOLA).");
        builder.Property(x => x.Soportado).HasComment("core-consultas soporta el impuesto del departamento (caché del rpc, R-23).");
        builder.Property(x => x.ActualizadoEn).HasComment("Último refresco desde core-consultas (timestamptz).");
    }
}
