using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Una fila por compañía con el estado de su aprovisionamiento en el API de liquidación (plan §3.1). Cada transición sube
/// <see cref="Version"/> y deja una fila en <see cref="HistorialAprovisionamiento"/>; <see cref="ReportadoVersion"/> es la última
/// versión reportada a la plataforma. Sin secretos ni PII (el motivo es un código y un detalle técnico).
/// </summary>
public sealed class AprovisionamientoEmpresa : IEntidadDeEmpresa
{
    public Guid TenantId { get; set; }

    public EstadoAprovisionamiento Estado { get; set; } = EstadoAprovisionamiento.Pendiente;

    public TipoErrorAprovisionamiento? ErrorTipo { get; set; }

    public string? MotivoCodigo { get; set; }

    public string? MotivoDetalle { get; set; }

    public EstadoAprovisionamiento? EstadoPrevio { get; set; }

    public bool FirmaConfirmada { get; set; }

    public bool ModoRespaldoFirma { get; set; }

    public int FirmasInvalidasConsecutivas { get; set; }

    public int IntentosAutomaticos { get; set; }

    public DateTimeOffset? ProximoIntentoEn { get; set; }

    /// <summary>eventId del último <c>impuestos.producto.cambiado</c> aplicado.</summary>
    public Guid? UltimoCambioPlataformaId { get; set; }

    public long Version { get; set; } = 1;

    public long ReportadoVersion { get; set; }

    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ActualizadoEn { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary><c>impuestos.aprovisionamientos</c> (plan §4.1).</summary>
internal sealed class AprovisionamientoEmpresaConfig : IEntityTypeConfiguration<AprovisionamientoEmpresa>
{
    public void Configure(EntityTypeBuilder<AprovisionamientoEmpresa> builder)
    {
        var estados = CodigosDeEnum.ListaSql<EstadoAprovisionamiento>();
        builder.ToTable("aprovisionamientos", t =>
        {
            t.HasComment("Estado del aprovisionamiento de cada compañía en el API de liquidación (plan §3.1). Sin secretos ni PII.");
            t.HasCheckConstraint("ck_aprovisionamientos_estado", $"estado IN ({estados})");
            t.HasCheckConstraint("ck_aprovisionamientos_estado_previo", $"estado_previo IS NULL OR estado_previo IN ({estados})");
            t.HasCheckConstraint("ck_aprovisionamientos_error_tipo", $"error_tipo IS NULL OR error_tipo IN ({CodigosDeEnum.ListaSql<TipoErrorAprovisionamiento>()})");
        });
        builder.HasKey(x => x.TenantId).HasName("pk_aprovisionamientos");
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Property(x => x.Estado).HasConversion(CodigosDeEnum.Convertidor<EstadoAprovisionamiento>()).HasMaxLength(32);
        builder.Property(x => x.ErrorTipo).HasConversion(CodigosDeEnum.Convertidor<TipoErrorAprovisionamiento>()).HasMaxLength(16);
        builder.Property(x => x.MotivoCodigo).HasMaxLength(64);
        builder.Property(x => x.MotivoDetalle).HasMaxLength(500).HasComment("Detalle técnico del motivo, sin secretos ni PII.");
        builder.Property(x => x.EstadoPrevio).HasConversion(CodigosDeEnum.Convertidor<EstadoAprovisionamiento>()).HasMaxLength(32);
        builder.Property(x => x.FirmaConfirmada).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.ModoRespaldoFirma).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.FirmasInvalidasConsecutivas).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.IntentosAutomaticos).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.UltimoCambioPlataformaId).HasComment("eventId del último trabajo impuestos.producto.cambiado aplicado.");
        builder.Property(x => x.Version).HasDefaultValue(1L).ValueGeneratedNever();
        builder.Property(x => x.ReportadoVersion).HasDefaultValue(0L).ValueGeneratedNever();
        builder.Property(x => x.CreadoEn).HasDefaultValueSql("now()").ValueGeneratedNever();
        builder.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()").ValueGeneratedNever();
    }
}
