using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>Una transición del aprovisionamiento de una compañía (plan §3.1). Solo se inserta.</summary>
public sealed class HistorialAprovisionamiento : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    /// <summary>Estado anterior; null si la compañía no tenía fila.</summary>
    public EstadoAprovisionamiento? Desde { get; init; }

    public EstadoAprovisionamiento Hacia { get; init; }

    public string? MotivoCodigo { get; init; }

    public Guid? ActorId { get; init; }

    /// <summary>Quién provocó la transición (p. ej. <c>SISTEMA</c>, <c>SUPER_ADMIN</c>, <c>PLATAFORMA</c>).</summary>
    public required string ActorTipo { get; init; }

    public DateTimeOffset En { get; init; }
}

/// <summary><c>impuestos.historial_aprovisionamiento</c> (plan §4.1).</summary>
internal sealed class HistorialAprovisionamientoConfig : IEntityTypeConfiguration<HistorialAprovisionamiento>
{
    public void Configure(EntityTypeBuilder<HistorialAprovisionamiento> builder)
    {
        var estados = CodigosDeEnum.ListaSql<EstadoAprovisionamiento>();
        builder.ToTable("historial_aprovisionamiento", t =>
        {
            t.HasComment("Transiciones del aprovisionamiento por compañía (plan §3.1). Solo inserción.");
            t.HasCheckConstraint("ck_historial_aprovisionamiento_hacia", $"hacia IN ({estados})");
            t.HasCheckConstraint("ck_historial_aprovisionamiento_desde", $"desde IS NULL OR desde IN ({estados})");
        });
        builder.HasKey(x => x.Id).HasName("pk_historial_aprovisionamiento");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Desde).HasConversion(CodigosDeEnum.Convertidor<EstadoAprovisionamiento>()).HasMaxLength(32);
        builder.Property(x => x.Hacia).HasConversion(CodigosDeEnum.Convertidor<EstadoAprovisionamiento>()).HasMaxLength(32);
        builder.Property(x => x.MotivoCodigo).HasMaxLength(64);
        builder.Property(x => x.ActorTipo).HasMaxLength(16);
        builder.HasIndex(x => new { x.TenantId, x.En }).HasDatabaseName("ix_historial_aprovisionamiento_tenant_en");
    }
}
