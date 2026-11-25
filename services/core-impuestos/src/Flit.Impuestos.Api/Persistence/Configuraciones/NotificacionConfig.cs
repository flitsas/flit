using Flit.Impuestos.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>Aviso en pantalla de la compañía (p. ej. «lote terminado», DA-6b) con los usuarios que ya lo leyeron.</summary>
public sealed class Notificacion : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid LiquidacionId { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public DateTimeOffset CreadaEn { get; set; }

    public Guid[] LeidaPor { get; set; } = [];
}

/// <summary><c>impuestos.notificaciones</c> (plan §4.1).</summary>
internal sealed class NotificacionConfig : IEntityTypeConfiguration<Notificacion>
{
    public void Configure(EntityTypeBuilder<Notificacion> builder)
    {
        builder.ToTable("notificaciones", t => t.HasComment("Avisos en pantalla de la compañía (DA-6b). Sin PII."));
        builder.HasKey(x => x.Id).HasName("pk_notificaciones");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Tipo).HasMaxLength(32);
        builder.Property(x => x.LeidaPor).HasColumnType("uuid[]").HasDefaultValueSql("'{}'").ValueGeneratedNever();
        builder.HasIndex(x => new { x.TenantId, x.CreadaEn }).HasDatabaseName("ix_notificaciones_tenant_creada");
    }
}
