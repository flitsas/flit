using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.saneamientos</c> (HU #14576, schema.md §3.1/§5.2, ADR-0087): clase (a) de ADR-0072 con <c>xmin</c>, FK compuestas
/// <c>(tenant_id, x_id)</c> RESTRICT contra el lote y el vehículo, CHECK de coherencia de estado y un solo no anulado por vehículo
/// (<see cref="IndiceVigente"/>). <c>ix_saneamientos_bandeja</c> no lleva <c>tenant_id</c> al frente: excepción A11 de ADR-0087 (bandeja
/// de FLIT entre compañías, solo en <c>Modulos/Backoffice</c>).
/// </summary>
internal sealed class SaneamientoConfig : IEntityTypeConfiguration<Saneamiento>
{
    /// <summary>Único parcial: <c>EscritorSaneamiento</c> traduce su 23505 a <c>409 YA_SOLICITADO</c>.</summary>
    public const string IndiceVigente = "ux_saneamiento_vigente";

    public void Configure(EntityTypeBuilder<Saneamiento> builder)
    {
        static string C(EstadoSaneamiento e) => CodigosDeEnum.Codigo(e);
        var (sol, ges, san, nop, anu) = (C(EstadoSaneamiento.Solicitado), C(EstadoSaneamiento.EnGestion),
            C(EstadoSaneamiento.Saneado), C(EstadoSaneamiento.NoProcede), C(EstadoSaneamiento.Anulado));

        builder.ToTable("saneamientos", t =>
        {
            t.HasComment("Saneamiento de un vehículo (clase (a) de ADR-0072, ADR-0087): solicitud del cliente y gestión de FLIT, sin dinero. Un solo no anulado por vehículo. Sin borrado: ANULADO es un estado.");
            t.HasCheckConstraint("ck_saneamientos_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoSaneamiento>()})");
            t.HasCheckConstraint("ck_saneamientos_tomado",
                $"(tomado_por IS NULL) = (tomado_en IS NULL) AND (estado <> '{sol}' OR tomado_en IS NULL) AND (estado NOT IN ('{ges}', '{san}', '{nop}') OR tomado_en IS NOT NULL)");
            t.HasCheckConstraint("ck_saneamientos_cierre",
                $"(cerrado_por IS NULL) = (cerrado_en IS NULL) AND (estado IN ('{san}', '{nop}')) = (cerrado_en IS NOT NULL)");
            t.HasCheckConstraint("ck_saneamientos_motivo_cierre",
                $"(estado = '{nop}') = (motivo_cierre IS NOT NULL) AND (motivo_cierre IS NULL OR char_length(btrim(motivo_cierre)) BETWEEN 10 AND 500)");
            t.HasCheckConstraint("ck_saneamientos_anulacion",
                $"(anulado_por IS NULL) = (anulado_en IS NULL) AND (estado = '{anu}') = (anulado_en IS NOT NULL) AND anulado_por_flit = (motivo_anulacion IS NOT NULL) AND (NOT anulado_por_flit OR anulado_en IS NOT NULL)");
            t.HasCheckConstraint("ck_saneamientos_motivo_anulacion",
                "motivo_anulacion IS NULL OR char_length(btrim(motivo_anulacion)) BETWEEN 10 AND 500");
            t.HasCheckConstraint("ck_saneamientos_observacion", "observacion_flit IS NULL OR btrim(observacion_flit) <> ''");
        });
        builder.HasKey(x => x.Id).HasName("pk_saneamientos");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_saneamientos_tenant_id");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote y del vehículo.");
        builder.Property(x => x.DiagnosticoId).HasComment("Lote del vehículo.");
        builder.Property(x => x.VehiculoId).HasComment("Vehículo a sanear.");
        builder.Property(x => x.Estado).HasConversion(CodigosDeEnum.Convertidor<EstadoSaneamiento>()).HasMaxLength(16)
            .HasComment("SOLICITADO, EN_GESTION, SANEADO, NO_PROCEDE o ANULADO (final).");
        builder.Property(x => x.SolicitadoPor).HasComment("@pii:low Usuario de la compañía que marcó (claim sub, seudónimo).");
        builder.Property(x => x.SolicitadoEn).HasComment("Instante de la solicitud (timestamptz). Orden de la bandeja.");
        builder.Property(x => x.TomadoPor).HasComment("@pii:low Usuario FLIT que lo tomó por primera vez; no cambia al reabrir.");
        builder.Property(x => x.TomadoEn).HasComment("Instante de la primera toma; no cambia al reabrir.");
        builder.Property(x => x.CerradoPor).HasComment("@pii:low Usuario FLIT que cerró; se limpia al reabrir.");
        builder.Property(x => x.CerradoEn).HasComment("Instante del cierre (SANEADO o NO_PROCEDE); se limpia al reabrir.");
        builder.Property(x => x.MotivoCierre).HasMaxLength(500)
            .HasComment("@pii:low Motivo de NO_PROCEDE (texto de FLIT, 10..500); visible al cliente; nunca en logs ni avisos.");
        builder.Property(x => x.AnuladoPor).HasComment("@pii:low Quien anuló: el cliente (desmarcar) o FLIT.");
        builder.Property(x => x.AnuladoEn).HasComment("Instante de la anulación.");
        builder.Property(x => x.AnuladoPorFlit).HasDefaultValueSql("false").ValueGeneratedNever()
            .HasComment("True si anuló FLIT (exige motivo); false si desmarcó el cliente o no está anulado.");
        builder.Property(x => x.MotivoAnulacion).HasMaxLength(500)
            .HasComment("@pii:low Motivo de la anulación de FLIT (10..500); visible al cliente; nunca en logs ni avisos.");
        builder.Property(x => x.ObservacionFlit).HasMaxLength(1000)
            .HasComment("@pii:low Observación general de FLIT, visible al cliente; editable en EN_GESTION.");
        foreach (var inmutable in new[] { nameof(Saneamiento.DiagnosticoId), nameof(Saneamiento.VehiculoId),
                     nameof(Saneamiento.SolicitadoPor), nameof(Saneamiento.SolicitadoEn) })
            builder.Property(inmutable).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_saneamientos_diagnosticos").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VehiculoDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehiculoId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_saneamientos_vehiculos").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.VehiculoId }, IndiceVigente).IsUnique().HasFilter($"estado <> '{anu}'").HasDatabaseName(IndiceVigente);
        // Cubre fk_saneamientos_vehiculos: el único es parcial (precedente ix_derivaciones_vehiculo).
        builder.HasIndex(x => new { x.TenantId, x.VehiculoId }, "ix_saneamientos_vehiculo").HasDatabaseName("ix_saneamientos_vehiculo");
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId }).HasDatabaseName("ix_saneamientos_diagnostico");
        // Bandeja de FLIT entre compañías (excepción A11, ADR-0087). Solo la usa Modulos/Backoffice.
        builder.HasIndex(x => new { x.Estado, x.SolicitadoEn, x.Id }).HasDatabaseName("ix_saneamientos_bandeja");

        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
