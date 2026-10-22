using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.consultas</c> (HU #13723, plan §5, MOD03/MOD04): celda del semáforo y unidad de trabajo de la cola durable
/// (ADR a). Entidad de negocio, clase (a) de ADR-0072: <c>xmin</c>, sin borrado lógico. La base garantiza una celda por
/// vehículo y fuente (<c>ux_consulta_vehiculo_fuente</c>, empieza por <c>tenant_id</c> y cubre la FK al vehículo), que los
/// códigos son los de los enums del dominio y que el motivo de N/A existe si y solo si la celda está en N/A
/// (<c>ck_motivo_na</c>). Las tres FK son compuestas por empresa y <c>RESTRICT</c>: la celda cuelga de un lote y un vehículo
/// de su empresa, y solo reutiliza (D-10) el dato de una celda de su misma empresa.
/// </summary>
internal sealed class ConsultaConfig : IEntityTypeConfiguration<CeldaConsulta>
{
    public void Configure(EntityTypeBuilder<CeldaConsulta> builder)
    {
        var enCola = $"'{CodigosDeEnum.Codigo(EstadoCelda.Pendiente)}', '{CodigosDeEnum.Codigo(EstadoCelda.EnCurso)}'";
        builder.ToTable("consultas", t =>
        {
            t.HasComment("Celdas del semáforo por vehículo y fuente (MOD03/MOD04, HU #13723); unidad de trabajo de la cola durable.");
            t.HasCheckConstraint("ck_consultas_fuente", $"fuente IN ({CodigosDeEnum.ListaSql<Fuente>()})");
            t.HasCheckConstraint("ck_consultas_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoCelda>()})");
            t.HasCheckConstraint("ck_consultas_motivo_na", $"motivo_na IS NULL OR motivo_na IN ({CodigosDeEnum.ListaSql<MotivoNa>()})");
            t.HasCheckConstraint("ck_motivo_na", $"(estado = '{CodigosDeEnum.Codigo(EstadoCelda.Na)}') = (motivo_na IS NOT NULL)");
            t.HasCheckConstraint("ck_consultas_origen", $"origen IS NULL OR origen IN ({CodigosDeEnum.ListaSql<OrigenCelda>()})");
            t.HasCheckConstraint("ck_consultas_payload_objeto", "payload IS NULL OR jsonb_typeof(payload) = 'object'");
            t.HasCheckConstraint("ck_consultas_intentos_auto", "intentos_auto >= 0");
            t.HasCheckConstraint("ck_consultas_tipo_error", $"tipo_error IS NULL OR estado = '{CodigosDeEnum.Codigo(EstadoCelda.Error)}'");
            // HU #13746: el reproceso pendiente solo existe mientras la celda espera o se ejecuta.
            t.HasCheckConstraint("ck_consultas_reproceso_usuario", $"reproceso_usuario_id IS NULL OR estado IN ({enCola})");
            // HU #13800: el dato es MANUAL si y solo si hay usuario del llenado manual (con origen nulo tampoco hay usuario).
            t.HasCheckConstraint(
                "ck_consultas_origen_manual",
                $"(COALESCE(origen, '') = '{CodigosDeEnum.Codigo(OrigenCelda.Manual)}') = (usuario_manual_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id).HasName("pk_consultas");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_consultas_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote (la misma del vehículo).");
        builder.Property(x => x.DiagnosticoId).HasComment("Cabecera del lote.");
        builder.Property(x => x.VehiculoId).HasComment("Vehículo de la celda.");
        builder.Property(x => x.Fuente)
            .HasConversion(CodigosDeEnum.Convertidor<Fuente>()).HasMaxLength(32)
            .HasComment("Fuente de la celda (enum Fuente del dominio; sin FASECOLDA, 12659 #1).");
        builder.Property(x => x.Estado)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoCelda>()).HasMaxLength(16)
            .HasComment("PENDIENTE, EN_CURSO, COMPLETADO, ERROR o NA (plan §3.5).");
        builder.Property(x => x.MotivoNa)
            .HasConversion(CodigosDeEnum.Convertidor<MotivoNa>()).HasMaxLength(32)
            .HasComment("SERVICIO_PUBLICO, DEPARTAMENTO_NO_SOPORTADO o SIN_DOCUMENTO_LOCATARIO; solo con estado NA (R-9, R-3).");
        builder.Property(x => x.TipoError)
            .HasMaxLength(32)
            .HasComment("Causa del ERROR (HU #13730): código de TipoErrorFuente o BLOQUEADA_POR_RUNT; solo con estado ERROR.");
        builder.Property(x => x.Origen)
            .HasConversion(CodigosDeEnum.Convertidor<OrigenCelda>()).HasMaxLength(8)
            .HasComment("API o MANUAL; null mientras no hay dato.");
        builder.Property(x => x.Payload)
            .HasColumnType("jsonb")
            .HasComment("@pii:medium Resultado normalizado de la fuente (objeto JSON), nunca el crudo del proveedor (R-13); sin documentos en claro.");
        builder.Property(x => x.UsuarioManualId).HasComment("@pii:low Usuario del backoffice que la llenó a mano (claim sub); nunca en DTO de cliente.");
        builder.Property(x => x.DatoObtenidoEn).HasComment("Fecha y hora del dato que muestra la celda (D-10); la de la celda origen si se reutilizó.");
        builder.Property(x => x.ReutilizadaDeId).HasComment("Celda de la misma empresa y del mismo día cuyo dato se reutilizó sin consumo (D-10).");
        builder.Property(x => x.DisponibleDesde)
            .HasDefaultValueSql("now()").ValueGeneratedNever()
            .HasComment("Cola: desde cuándo el procesador la puede tomar (backoff, D-12).");
        builder.Property(x => x.LeaseHasta).HasComment("Cola: vencimiento del lease del procesador; null si nadie la tiene.");
        builder.Property(x => x.IntentosAuto)
            .HasDefaultValue(0).ValueGeneratedNever()
            .HasComment("Cola: intentos automáticos hechos (reintentos de errores transitorios, D-12).");
        builder.Property(x => x.ReprocesoUsuarioId)
            .HasComment("@pii:low Usuario del reproceso pendiente (claim sub, HU #13746, R-1): la siguiente ejecución deja su intento con disparador REPROCESO y este usuario; null si no hay reproceso pendiente.");
        builder.Property(x => x.ActualizadoEn).HasComment("Último cambio de la celda (timestamptz).");
        builder.Property(x => x.VersionTablero).HasComment("Versión del tablero del lote en el último cambio (polling ?desde=).");
        builder.Property(x => x.DiagnosticoId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.VehiculoId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(x => x.Fuente).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasOne<VehiculoDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehiculoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_consultas_vehiculos")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_consultas_diagnosticos")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CeldaConsulta>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ReutilizadaDeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_consultas_consultas_reutilizada_de")
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotencia (12659 #5): una celda por vehículo y fuente. Como el vehículo fija la empresa, la unicidad es la misma
        // que (vehiculo_id, fuente); empezar por tenant_id cubre la FK al vehículo.
        builder.HasIndex(x => new { x.TenantId, x.VehiculoId, x.Fuente }).IsUnique().HasDatabaseName("ux_consulta_vehiculo_fuente");
        // Polling del tablero; cubre la FK al lote.
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId, x.VersionTablero }).HasDatabaseName("ix_consultas_tablero");
        // Cubre la FK de reutilización.
        builder.HasIndex(x => new { x.TenantId, x.ReutilizadaDeId }).HasDatabaseName("ix_consultas_reutilizada_de");
        // Reutilización del mismo día (D-10): el dato completado más reciente de la empresa por fuente.
        builder.HasIndex(x => new { x.TenantId, x.Fuente, x.DatoObtenidoEn })
            .HasFilter($"estado = '{CodigosDeEnum.Codigo(EstadoCelda.Completado)}'")
            .HasDatabaseName("ix_consultas_reutilizacion");
        // Cola durable de todas las empresas: el procesador toma con FOR UPDATE SKIP LOCKED (ADR a, #13727).
        builder.HasIndex(x => x.DisponibleDesde).HasFilter($"estado IN ({enCola})").HasDatabaseName("ix_cola");

        // Concurrencia optimista con la columna de sistema xmin: no agrega columnas a la tabla.
        builder.Property(x => x.Version).IsRowVersion();
    }
}
