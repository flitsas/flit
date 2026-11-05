using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Persistence.Siembra;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.definiciones_fuentes_obligatorias</c> (ADR-0078). Catálogo global de FLIT, igual para todas las
/// empresas. Excepción de ADR-0072: sin <c>tenant_id</c>, sin filtro, sin <c>deleted_*</c> y sin <c>is_active</c>. La
/// semilla sale del proyector, no de códigos copiados. La cabecera del unilateral entra en <c>HasData</c>.
/// </summary>
internal sealed class DefinicionFuentesObligatoriasConfig : IEntityTypeConfiguration<DefinicionFuentesObligatorias>
{
    private const string Patron = "^[A-Z][A-Z0-9_]{0,63}$";

    public void Configure(EntityTypeBuilder<DefinicionFuentesObligatorias> builder)
    {
        builder.ToTable("definiciones_fuentes_obligatorias", t =>
        {
            t.HasComment(
                "Catálogo global de definiciones de fuentes obligatorias de FLIT (ADR-0078). Sin tenant_id: excepción de catálogo de ADR-0072. La cabecera existe o no existe; cero líneas es la lista vacía explícita.");
            t.HasCheckConstraint(
                "ck_definicion_un_alcance",
                $"(familia IS NOT NULL AND tipo_tramite_codigo IS NULL AND familia ~ '{Patron}') OR (familia IS NULL AND tipo_tramite_codigo IS NOT NULL AND tipo_tramite_codigo ~ '{Patron}')");
        });
        builder.HasKey(x => x.Id).HasName("pk_definiciones_fuentes_obligatorias");
        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasComment("UUID v5 en la semilla y UUID v7 si el PUT crea la cabecera. Sin DEFAULT uuidv7().");
        builder.Property(x => x.Familia).HasMaxLength(64).HasComment("Familia del trámite. Nula si la definición es de un tipo.");
        builder.Property(x => x.TipoTramiteCodigo).HasMaxLength(64).HasComment("Código del tipo de trámite. Nulo si la definición es de una familia.");
        builder.Property(x => x.ActualizadoEn).HasComment("Instante del último guardado (timestamptz). En la semilla, 2026-11-04T00:00:00Z.");
        builder.Property(x => x.ActualizadoPor)
            .HasComment("@pii:low Seudónimo uuid del claim sub de quien guardó la definición; sin nombre, correo ni documento. NULL en la semilla.");

        builder.HasIndex(x => x.Familia)
            .IsUnique()
            .HasFilter("familia IS NOT NULL")
            .HasDatabaseName("ux_definicion_familia");
        builder.HasIndex(x => x.TipoTramiteCodigo)
            .IsUnique()
            .HasFilter("tipo_tramite_codigo IS NOT NULL")
            .HasDatabaseName("ux_definicion_tipo");

        builder.HasData(ProyectorFuentesObligatorias.Cabeceras().Select(fila => (object)new
        {
            fila.Id,
            fila.Familia,
            fila.TipoTramiteCodigo,
            fila.ActualizadoEn,
            fila.ActualizadoPor,
        }).ToArray());

        // Concurrencia con la columna de sistema xmin. La línea no lleva token: se reemplaza el conjunto.
        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
