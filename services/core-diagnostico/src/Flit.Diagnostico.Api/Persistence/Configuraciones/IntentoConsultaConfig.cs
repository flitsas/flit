using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.intentos_consulta</c> (HU #13723, plan §5, R-1): auditoría de solo inserción de cada intento a una fuente,
/// clase (b) de ADR-0072: sin columnas de edición, borrado lógico ni token de concurrencia. Las FK a la celda, al vehículo y
/// al lote son compuestas por empresa y <c>RESTRICT</c> (un historial no se borra en cascada). Cada índice empieza por
/// <c>tenant_id</c> y cubre su FK.
/// </summary>
internal sealed class IntentoConsultaConfig : IEntityTypeConfiguration<IntentoConsulta>
{
    public void Configure(EntityTypeBuilder<IntentoConsulta> builder)
    {
        builder.ToTable("intentos_consulta", t =>
        {
            t.HasComment("Intentos de consulta por celda: auditoría y log de negocio (R-1, HU #13723). Solo inserción.");
            t.HasCheckConstraint("ck_intentos_consulta_fuente", $"fuente IN ({CodigosDeEnum.ListaSql<Fuente>()})");
            t.HasCheckConstraint("ck_intentos_consulta_servicio", $"servicio IS NULL OR servicio IN ({CodigosDeEnum.ListaSql<ServicioConsulta>()})");
            t.HasCheckConstraint("ck_intentos_consulta_disparador", $"disparador IN ({CodigosDeEnum.ListaSql<DisparadorIntento>()})");
            t.HasCheckConstraint("ck_intentos_consulta_estado", $"estado IN ({CodigosDeEnum.ListaSql<ResultadoIntento>()})");
        });
        builder.HasKey(x => x.Id).HasName("pk_intentos_consulta");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña de la celda.");
        builder.Property(x => x.DiagnosticoId).HasComment("Cabecera del lote.");
        builder.Property(x => x.VehiculoId).HasComment("Vehículo de la celda.");
        builder.Property(x => x.ConsultaId).HasComment("Celda del intento.");
        builder.Property(x => x.Fuente).HasConversion(CodigosDeEnum.Convertidor<Fuente>()).HasMaxLength(32).HasComment("Fuente consultada.");
        builder.Property(x => x.Servicio)
            .HasConversion(CodigosDeEnum.Convertidor<ServicioConsulta>()).HasMaxLength(16)
            .HasComment("Proveedor que respondió (12659 #2); null si no llegó a ninguno o fue manual.");
        builder.Property(x => x.Disparador)
            .HasConversion(CodigosDeEnum.Convertidor<DisparadorIntento>()).HasMaxLength(16)
            .HasComment("AUTO, REPROCESO o MANUAL (R-1).");
        builder.Property(x => x.Inicio).HasComment("Inicio del intento (timestamptz).");
        builder.Property(x => x.Fin).HasComment("Fin del intento (timestamptz).");
        builder.Property(x => x.Estado).HasConversion(CodigosDeEnum.Convertidor<ResultadoIntento>()).HasMaxLength(8).HasComment("SUCCESS o ERROR.");
        builder.Property(x => x.TipoError).HasMaxLength(32).HasComment("Tipo de error de la fuente (patrón Comparendos); null si fue exitoso.");
        builder.Property(x => x.MensajeError).HasComment("@pii:low Mensaje de error de la fuente; sin documentos en claro.");
        builder.Property(x => x.TransaccionProveedorId).HasMaxLength(128).HasComment("Transacción del proveedor; null en el manual (12659 #6).");
        builder.Property(x => x.DesdeCache)
            .HasDefaultValue(false).ValueGeneratedNever()
            .HasComment("El dato salió de la reutilización del mismo día (D-10), sin consumo.");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Usuario del reproceso o del manual (claim sub); null si fue el procesador.");

        builder.HasOne<CeldaConsulta>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ConsultaId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_intentos_consulta_consultas")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VehiculoDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehiculoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_intentos_consulta_vehiculos")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_intentos_consulta_diagnosticos")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.ConsultaId, x.Inicio }).HasDatabaseName("ix_intentos_consulta_celda");
        builder.HasIndex(x => new { x.TenantId, x.VehiculoId }).HasDatabaseName("ix_intentos_consulta_vehiculo");
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId }).HasDatabaseName("ix_intentos_consulta_diagnostico");
        // Métricas y alertas de integración por fuente y servicio (plan §10).
        builder.HasIndex(x => new { x.TenantId, x.Fuente, x.Servicio, x.Inicio }).HasDatabaseName("ix_intentos_metricas");
    }
}
