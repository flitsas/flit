using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.dictamenes</c> (corte 4, ADR-0077). Clase (a) de ADR-0072: <c>tenant_id</c>, filtro global, <c>xmin</c>
/// y sin <c>deleted_*</c>. La FK es compuesta <c>(tenant_id, vehiculo_id)</c> contra el alterno del vehículo, con
/// <c>ON DELETE RESTRICT</c> y sin <c>ON UPDATE</c>. Nace vacía: no hay <c>HasData</c>. <c>ix_redaccion_pendiente</c>
/// (corte 6, ADR-0072) no empieza por <c>tenant_id</c>: el trabajador reclama filas de todas las empresas.
/// <see cref="DictamenVehiculo.Version"/> es la versión de negocio; el token es <see cref="DictamenVehiculo.Xmin"/>.
/// </summary>
internal sealed class DictamenVehiculoConfig : IEntityTypeConfiguration<DictamenVehiculo>
{
    public void Configure(EntityTypeBuilder<DictamenVehiculo> builder)
    {
        builder.ToTable("dictamenes", t =>
        {
            t.HasComment(
                "Dictamen de un vehículo (clase (a) de ADR-0072, ADR-0077): tenant_id, xmin y FK compuesta al vehículo. Nace vacía, sin borrado lógico.");
            t.HasCheckConstraint("ck_dictamenes_veredicto", "veredicto IN ('VIABLE', 'BLOQUEADO')");
            t.HasCheckConstraint("ck_dictamenes_alertamientos", "jsonb_typeof(alertamientos) = 'array'");
            t.HasCheckConstraint("ck_dictamenes_redactado_por", "redactado_por IN ('PLANTILLA', 'LLM', 'MANUAL')");
            t.HasCheckConstraint("ck_dictamenes_estado_redaccion", "estado_redaccion IN ('PENDIENTE', 'COMPLETADA')");
            t.HasCheckConstraint("ck_redaccion", "(estado_redaccion = 'COMPLETADA') = (redactado_por IN ('LLM', 'MANUAL'))");
            // R7_AjusteDictamen (ADR-0086, schema.md §2.2): coherencia del override y cadena de versiones del ajuste FLIT.
            t.HasCheckConstraint("ck_dictamenes_version", "version >= 1");
            t.HasCheckConstraint(
                "ck_dictamenes_override",
                "(usuario_override_id IS NULL) = (override_en IS NULL) AND analisis_override = (usuario_override_id IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_dictamenes_ajuste",
                "analisis_override = (ajustado_desde_version IS NOT NULL) AND (ajustado_desde_version IS NULL OR ajustado_desde_version = version - 1)");
            t.HasCheckConstraint(
                "ck_dictamenes_ajuste_motivo",
                "ajuste_motivo IS NULL OR (analisis_override AND char_length(btrim(ajuste_motivo)) BETWEEN 10 AND 500)");
            t.HasCheckConstraint(
                "ck_dictamenes_manual",
                "redactado_por <> 'MANUAL' OR (analisis_override AND ajuste_motivo IS NOT NULL)");
        });
        builder.HasKey(x => x.Id).HasName("pk_dictamenes");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_dictamenes_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del dictamen (la misma del vehículo).");
        builder.Property(x => x.VehiculoId).HasComment("Vehículo dictaminado.");
        builder.Property(x => x.Version).HasComment("Versión de negocio del dictamen de ese vehículo. No es el token xmin.");
        builder.Property(x => x.Vigente).HasComment("El que se entrega hoy; false conserva el anterior (no se borra).");
        builder.Property(x => x.Veredicto).HasMaxLength(16).HasComment("VIABLE o BLOQUEADO. Lo calcula el evaluador.");
        builder.Property(x => x.Alertamientos)
            .HasColumnType("jsonb")
            .HasComment("Arreglo JSON: codigo, texto, efecto y fuente. Sin documentos ni nombres.");
        builder.Property(x => x.CatalogoVersion).HasComment("Versión del catálogo con la que se dictaminó. Entero de recuerdo, no FK.");
        builder.Property(x => x.RedactadoPor).HasMaxLength(16).HasComment("PLANTILLA, LLM o MANUAL (textos editados por FLIT; el LLM no los reescribe).");
        builder.Property(x => x.EstadoRedaccion).HasMaxLength(16).HasComment("PENDIENTE o COMPLETADA.");
        builder.Property(x => x.RedaccionIntentos)
            .HasDefaultValue(0)
            .ValueGeneratedNever()
            .HasComment("Intentos de la cola de redacción. La toma entre empresas los suma.");
        builder.Property(x => x.RedaccionDisponibleDesde).HasComment("Desde cuándo la cola puede tomar la redacción; null si no espera.");
        builder.Property(x => x.RedaccionLeaseHasta).HasComment("Vencimiento del lease de redacción; null si nadie la tiene.");
        builder.Property(x => x.AnalisisOverride)
            .HasDefaultValueSql("false")
            .ValueGeneratedNever()
            .HasComment("True si un analista reemplazó el veredicto. Ningún endpoint lo usa en este corte.");
        builder.Property(x => x.UsuarioOverrideId)
            .HasComment("@pii:low Seudónimo uuid de quien hizo el override; sin documento ni nombre.");
        builder.Property(x => x.OverrideEn).HasComment("Instante del override (timestamptz); null si no hubo.");
        builder.Property(x => x.AjusteMotivo)
            .HasMaxLength(500)
            .HasComment("@pii:low Motivo del ajuste FLIT (texto libre de soporte, 10..500). Solo backoffice; nunca en DTO de compañía, eventos ni logs. [ADR-0086]");
        builder.Property(x => x.AjustadoDesdeVersion)
            .HasComment("Versión vigente que reemplazó el override o ajuste FLIT (= version - 1). NULL en las automáticas. [ADR-0086]");
        builder.Property(x => x.CreadoEn).HasComment("Instante en que se guardó el dictamen (timestamptz).");

        builder.HasOne<VehiculoDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehiculoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_dictamenes_vehiculos")
            .OnDelete(DeleteBehavior.Restrict);

        // Único por versión. Empieza por tenant_id y cubre la FK compuesta al vehículo.
        builder.HasIndex(x => new { x.TenantId, x.VehiculoId, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_dictamen_version");
        builder.HasIndex(x => new { x.TenantId, x.VehiculoId })
            .IsUnique()
            .HasFilter("vigente")
            .HasDatabaseName("ux_dictamen_vigente");
        // Cola de redacción de todas las empresas (corte 6, ADR-0072). No empieza por tenant_id: el trabajador reclama y después fija la empresa.
        builder.HasIndex(x => x.RedaccionDisponibleDesde)
            .HasFilter("estado_redaccion = 'PENDIENTE' AND vigente")
            .HasDatabaseName("ix_redaccion_pendiente");

        // xmin sobre Xmin. Version (int) es la columna de negocio version, no el token.
        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
