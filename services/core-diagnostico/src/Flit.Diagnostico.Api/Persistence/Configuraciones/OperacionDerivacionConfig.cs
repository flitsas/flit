using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.derivacion_operaciones</c> (Punto 4, diseño §4, ADR-0084): cabecera de un «Crear trámite» individual o masivo.
/// Clase (a) de ADR-0072: <c>tenant_id</c>, filtro global, <c>xmin</c>, sin borrado. FK compuesta <c>(tenant_id, diagnostico_id)</c>
/// RESTRICT contra el alterno del lote; el alterno <c>(tenant_id, id)</c> sostiene la FK de los ítems. CHECK de tipo, estado, total,
/// contadores (nunca más que el total) y TERMINADA ⇔ <c>terminada_en</c>. PII: solo <c>usuario_id</c> (<c>@pii:low</c>, seudónimo uuid).
/// </summary>
internal sealed class OperacionDerivacionConfig : IEntityTypeConfiguration<OperacionDerivacion>
{
    public void Configure(EntityTypeBuilder<OperacionDerivacion> builder)
    {
        builder.ToTable("derivacion_operaciones", t =>
        {
            t.HasComment("Operación «Crear trámite» (Punto 4, ADR-0084): individual o masiva, de un lote y un usuario, con contadores de sus ítems.");
            t.HasCheckConstraint("ck_derivacion_operaciones_tipo", $"tipo IN ({CodigosDeEnum.ListaSql<TipoOperacionDerivacion>()})");
            t.HasCheckConstraint("ck_derivacion_operaciones_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoOperacionDerivacion>()})");
            t.HasCheckConstraint("ck_derivacion_operaciones_total", "total > 0");
            t.HasCheckConstraint(
                "ck_derivacion_operaciones_contadores",
                "creadas >= 0 AND omitidas >= 0 AND fallidas >= 0 AND creadas + omitidas + fallidas <= total");
            t.HasCheckConstraint(
                "ck_derivacion_operaciones_terminada",
                $"(estado = '{CodigosDeEnum.Codigo(EstadoOperacionDerivacion.Terminada)}') = (terminada_en IS NOT NULL)");
        });
        builder.HasKey(x => x.Id).HasName("pk_derivacion_operaciones");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_derivacion_operaciones_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote y de la operación.");
        builder.Property(x => x.DiagnosticoId).HasComment("Lote del que salen los vehículos.");
        builder.Property(x => x.Tipo)
            .HasConversion(CodigosDeEnum.Convertidor<TipoOperacionDerivacion>()).HasMaxLength(16)
            .HasComment("INDIVIDUAL (síncrona, una placa) o MASIVA (encolada).");
        builder.Property(x => x.TipoTramiteCodigo).HasMaxLength(64).HasComment("Tipo de trámite de los borradores (tramites.procedure_types.code).");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Usuario que lanzó la operación (claim sub, seudónimo); autor de los borradores (4.6).");
        builder.Property(x => x.Estado)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoOperacionDerivacion>()).HasMaxLength(16)
            .HasComment("EN_CURSO o TERMINADA (creadas + omitidas + fallidas = total).");
        builder.Property(x => x.Total).HasComment("Ítems de la operación.");
        builder.Property(x => x.Creadas).HasDefaultValue(0).ValueGeneratedNever().HasComment("Ítems CREADO.");
        builder.Property(x => x.Omitidas).HasDefaultValue(0).ValueGeneratedNever().HasComment("Ítems OMITIDO.");
        builder.Property(x => x.Fallidas).HasDefaultValue(0).ValueGeneratedNever().HasComment("Ítems FALLIDO.");
        builder.Property(x => x.CreadaEn).HasComment("Instante de creación de la operación.");
        builder.Property(x => x.TerminadaEn).HasComment("Instante en que cerró su último ítem; null mientras está EN_CURSO.");

        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_derivacion_operaciones_diagnostico")
            .OnDelete(DeleteBehavior.Restrict);

        // Última operación del lote (para retomar el progreso) y cobertura de la FK compuesta.
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.CreadaEn })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_derivacion_operaciones_lote");

        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
