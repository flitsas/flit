using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.historial_estado_diagnostico</c> (HU #13738, plan §5, R-8): auditoría de solo inserción de las transiciones
/// del lote. Como <c>cambios_contrato</c>, no lleva columnas de edición, borrado lógico ni <c>row_version</c> (ADR-0072). La FK
/// es compuesta <c>(tenant_id, diagnostico_id)</c> contra el alterno de la cabecera: la fila y su lote son de la misma empresa
/// aunque se escriba por SQL directo. Su índice empieza por <c>tenant_id</c> y cubre la FK.
/// </summary>
internal sealed class HistorialEstadoDiagnosticoConfig : IEntityTypeConfiguration<HistorialEstadoDiagnostico>
{
    public void Configure(EntityTypeBuilder<HistorialEstadoDiagnostico> builder)
    {
        var estados = CodigosDeEnum.ListaSql<EstadoDiagnostico>();
        builder.ToTable("historial_estado_diagnostico", t =>
        {
            t.HasComment("Transiciones de estado del lote (R-8, HU #13738). Solo inserción.");
            t.HasCheckConstraint("ck_historial_estado_diagnostico_estado_nuevo", $"estado_nuevo IN ({estados})");
            t.HasCheckConstraint("ck_historial_estado_diagnostico_estado_anterior", $"estado_anterior IS NULL OR estado_anterior IN ({estados})");
            t.HasCheckConstraint("ck_historial_estado_diagnostico_disparador", $"disparador IN ({CodigosDeEnum.ListaSql<DisparadorEstado>()})");
        });
        builder.HasKey(x => x.Id).HasName("pk_historial_estado_diagnostico");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote (la misma de la cabecera).");
        builder.Property(x => x.DiagnosticoId).HasComment("Cabecera del lote.");
        builder.Property(x => x.EstadoAnterior)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoDiagnostico>()).HasMaxLength(16)
            .HasComment("Estado de partida; null en la creación.");
        builder.Property(x => x.EstadoNuevo)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoDiagnostico>()).HasMaxLength(16)
            .HasComment("Estado de llegada.");
        builder.Property(x => x.Disparador)
            .HasConversion(CodigosDeEnum.Convertidor<DisparadorEstado>()).HasMaxLength(16)
            .HasComment("AUTO, CIERRE_PANEL, REPROCESO o MANUAL.");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Usuario que lo provocó (claim sub, seudónimo); null si fue el procesador.");
        builder.Property(x => x.En).HasComment("Instante de la transición (timestamptz).");

        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_historial_estado_diagnostico_diagnosticos")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.En }).HasDatabaseName("ix_historial_diagnostico");
    }
}
