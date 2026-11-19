using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.derivaciones</c> (Punto 4, diseño §4, ADR-0084): un ítem por vehículo de una operación. Clase (a) de ADR-0072.
/// FK compuestas RESTRICT a la operación y al vehículo. CHECK: estado conocido, CREADO ⇔ <c>tramite_id</c>, OMITIDO/FALLIDO ⇒
/// motivo. <see cref="IndiceVivo"/>: un solo ítem PENDIENTE/ENVIADO/CREADO por vehículo (R-16; doble clic y reintentos nunca crean
/// dos). Los índices parciales de pendientes y en vuelo son del masivo (crédito por compañía y reconciliador, incremento 3).
/// <c>ix_derivaciones_vehiculo</c> (sin filtro) cubre la FK al vehículo: el índice parcial de vivos no la cubre. PII: solo
/// <c>reintentado_por</c> (<c>@pii:low</c>, seudónimo uuid).
/// </summary>
internal sealed class ItemDerivacionConfig : IEntityTypeConfiguration<ItemDerivacion>
{
    public const string IndiceVivo = "ux_derivaciones_vehiculo_vivo";

    public void Configure(EntityTypeBuilder<ItemDerivacion> builder)
    {
        var creado = CodigosDeEnum.Codigo(EstadoItemDerivacion.Creado);
        builder.ToTable("derivaciones", t =>
        {
            t.HasComment("Ítem de una operación «Crear trámite» (Punto 4, ADR-0084): un vehículo, su estado, motivo e intentos y el trámite creado.");
            t.HasCheckConstraint("ck_derivaciones_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoItemDerivacion>()})");
            t.HasCheckConstraint("ck_derivaciones_creado", $"(estado = '{creado}') = (tramite_id IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_derivaciones_motivo",
                $"estado NOT IN ('{CodigosDeEnum.Codigo(EstadoItemDerivacion.Omitido)}', '{CodigosDeEnum.Codigo(EstadoItemDerivacion.Fallido)}') OR motivo_codigo IS NOT NULL");
            t.HasCheckConstraint("ck_derivaciones_intentos", "intentos >= 0");
            t.HasCheckConstraint("ck_derivaciones_republicaciones", "republicaciones >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_derivaciones");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_derivaciones_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña de la operación y del vehículo.");
        builder.Property(x => x.OperacionId).HasComment("Operación a la que pertenece.");
        builder.Property(x => x.VehiculoId).HasComment("Vehículo a derivar (external_ref del borrador en Trámites).");
        builder.Property(x => x.Orden).HasComment("Orden de liberación dentro de la operación (el de la selección).");
        builder.Property(x => x.Estado)
            .HasConversion(CodigosDeEnum.Convertidor<EstadoItemDerivacion>()).HasMaxLength(16)
            .HasComment("PENDIENTE, ENVIADO, CREADO, OMITIDO o FALLIDO.");
        builder.Property(x => x.MotivoCodigo).HasMaxLength(40)
            .HasComment("Motivo de OMITIDO o FALLIDO: NO_VIABLE, YA_TIENE_TRAMITE, OT_NO_HABILITADO, TRAMITES_NO_DISPONIBLE…; null en los demás.");
        builder.Property(x => x.Intentos).HasDefaultValue((short)0).ValueGeneratedNever().HasComment("Llamadas al rpc hechas por este ítem.");
        builder.Property(x => x.DisponibleDesde).HasComment("Espera del reintento programado (masivo); null si no espera.");
        builder.Property(x => x.EnviadoEn).HasComment("Instante en que salió hacia Trámites (en vuelo).");
        builder.Property(x => x.TramiteId).HasComment("Borrador creado en Trámites; solo en CREADO.");
        builder.Property(x => x.TramiteNumero).HasMaxLength(32).HasComment("Número (reference_number) del borrador.");
        builder.Property(x => x.ActualizadoEn).HasComment("Último cambio de estado.");
        builder.Property(x => x.Republicaciones).HasDefaultValue((short)0).ValueGeneratedNever()
            .HasComment("Veces que el reconciliador re-publicó el ítem colgado (incremento 3); a la segunda queda FALLIDO.");
        builder.Property(x => x.ReintentadoPor)
            .HasComment("@pii:low Usuario que reabrió el ítem con «reintentar fallidas» (claim sub, seudónimo); autor del borrador reintentado. Null si nunca se reintentó.");

        builder.HasOne<OperacionDerivacion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OperacionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_derivaciones_operacion")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VehiculoDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehiculoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_derivaciones_vehiculo")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.VehiculoId })
            .IsUnique()
            .HasFilter($"estado IN ('{CodigosDeEnum.Codigo(EstadoItemDerivacion.Pendiente)}', '{CodigosDeEnum.Codigo(EstadoItemDerivacion.Enviado)}', '{creado}')")
            .HasDatabaseName(IndiceVivo);
        // Cobertura de fk_derivaciones_vehiculo: el índice de vivos es parcial y no sirve al chequeo de la FK (revisión 2026-11-11).
        builder.HasIndex(x => new { x.TenantId, x.VehiculoId }, "ix_derivaciones_vehiculo")
            .HasDatabaseName("ix_derivaciones_vehiculo");
        builder.HasIndex(x => new { x.TenantId, x.OperacionId, x.Orden })
            .HasFilter($"estado = '{CodigosDeEnum.Codigo(EstadoItemDerivacion.Pendiente)}'")
            .HasDatabaseName("ix_derivaciones_pendientes");
        builder.HasIndex(x => new { x.TenantId, x.EnviadoEn })
            .HasFilter($"estado = '{CodigosDeEnum.Codigo(EstadoItemDerivacion.Enviado)}'")
            .HasDatabaseName("ix_derivaciones_en_vuelo");
        builder.HasIndex(x => new { x.TenantId, x.OperacionId, x.Estado }).HasDatabaseName("ix_derivaciones_operacion");

        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
