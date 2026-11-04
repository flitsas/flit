using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Persistence.Siembra;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.fuentes_definicion_obligatoria</c> (ADR-0078). Líneas del catálogo global. La PK compuesta cubre la
/// FK. <c>ON DELETE CASCADE</c> porque la línea no tiene historia propia. El CHECK es el enum <see cref="Fuente"/>
/// menos <see cref="Fuente.Runt"/>. <c>HasData</c> trae las tres líneas de la semilla; el unilateral no aporta.
/// </summary>
internal sealed class FuenteDefinicionObligatoriaConfig : IEntityTypeConfiguration<FuenteDefinicionObligatoria>
{
    public void Configure(EntityTypeBuilder<FuenteDefinicionObligatoria> builder)
    {
        var permitidas = string.Join(
            ", ",
            Enum.GetValues<Fuente>().Where(f => f != Fuente.Runt).Select(f => $"'{CodigosDeEnum.Codigo(f)}'"));

        builder.ToTable("fuentes_definicion_obligatoria", t =>
        {
            t.HasComment(
                "Fuentes de una definición obligatoria (ADR-0078). Sin tenant_id: excepción de catálogo de ADR-0072. Cero filas de una cabecera es la lista vacía explícita. RUNT no se guarda.");
            t.HasCheckConstraint("ck_fuentes_definicion_obligatoria_fuente", $"fuente IN ({permitidas})");
        });
        builder.HasKey(x => new { x.DefinicionId, x.Fuente }).HasName("pk_fuentes_definicion_obligatoria");
        builder.Property(x => x.DefinicionId).ValueGeneratedNever().HasComment("Cabecera dueña de la línea.");
        builder.Property(x => x.Fuente)
            .HasConversion(CodigosDeEnum.Convertidor<Fuente>())
            .HasMaxLength(32)
            .ValueGeneratedNever()
            .HasComment("Fuente configurable (enum Fuente menos RUNT).");

        builder.HasOne<DefinicionFuentesObligatorias>()
            .WithMany()
            .HasForeignKey(x => x.DefinicionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_fuentes_definicion_obligatoria_definicion");

        builder.HasData(ProyectorFuentesObligatorias.Lineas().Select(fila => (object)new
        {
            fila.DefinicionId,
            fila.Fuente,
        }).ToArray());
    }
}
