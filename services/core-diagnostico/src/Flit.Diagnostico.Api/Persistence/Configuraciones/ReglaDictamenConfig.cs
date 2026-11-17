using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Persistence.Siembra;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.reglas_dictamen</c> (corte 4, ADR-0077). Catálogo global de FLIT, igual para todas las empresas.
/// Excepción de ADR-0072: sin <c>tenant_id</c>, sin filtro, sin <c>deleted_*</c> y sin actor. La PK es un UUID v5
/// estable; la unicidad de negocio es <c>(catalogo_version, tipo_tramite_codigo, codigo)</c>. <c>condicion</c> repite
/// el código de la fila y no es un motor. La siembra sale del proyector, no de textos copiados.
/// </summary>
internal sealed class ReglaDictamenConfig : IEntityTypeConfiguration<ReglaDictamen>
{
    public void Configure(EntityTypeBuilder<ReglaDictamen> builder)
    {
        builder.ToTable("reglas_dictamen", t =>
        {
            t.HasComment(
                "Catálogo global de reglas de dictamen de FLIT (ADR-0077). Sin tenant_id: excepción de catálogo de ADR-0072. No se edita a mano; condicion repite el código de la fila y no es un motor.");
            t.HasCheckConstraint("ck_reglas_dictamen_efecto", "efecto IN ('BLOQUEA', 'ADVIERTE')");
            t.HasCheckConstraint(
                "ck_reglas_dictamen_condicion",
                "jsonb_typeof(condicion) = 'object' AND condicion->>'codigo' = codigo");
        });
        builder.HasKey(x => x.Id).HasName("pk_reglas_dictamen");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("UUID v5 del proyector (catalogo_version, tipo_tramite_codigo, codigo). Sin DEFAULT uuidv7().");
        builder.Property(x => x.CatalogoVersion).HasComment("Versión del catálogo. La v1 es 1.");
        builder.Property(x => x.TipoTramiteCodigo).HasMaxLength(64).HasComment("Código de trámite de la familia (varchar 64).");
        builder.Property(x => x.Codigo).HasMaxLength(64).HasComment("Código de la regla. La condición guarda el mismo valor.");
        builder.Property(x => x.Condicion)
            .HasColumnType("jsonb")
            .HasComment("Objeto {\"codigo\":\"<codigo>\"}. No es un motor de reglas.");
        builder.Property(x => x.Efecto).HasMaxLength(16).HasComment("BLOQUEA o ADVIERTE, el de CatalogoReglasDictamen.Filas.");
        builder.Property(x => x.PlantillaTexto).HasComment("Texto de la plantilla, el de CatalogoReglasDictamen.Filas.");
        builder.Property(x => x.Vigente).HasComment("La v1 nace en true. El único update previsto es false al publicar otra versión.");
        builder.HasIndex(x => new { x.CatalogoVersion, x.TipoTramiteCodigo, x.Codigo })
            .IsUnique()
            .HasDatabaseName("ux_regla");

        builder.HasData(ProyectorReglasDictamen.Proyectar().Select(fila => (object)new
        {
            fila.Id,
            fila.CatalogoVersion,
            fila.TipoTramiteCodigo,
            fila.Codigo,
            fila.Condicion,
            fila.Efecto,
            fila.PlantillaTexto,
            fila.Vigente,
        }).ToArray());

        // Concurrencia con la columna de sistema xmin. No hay columna version en esta tabla.
        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
