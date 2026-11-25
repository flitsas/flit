using Flit.Impuestos.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>Tipo de recibo PDF del API.</summary>
public enum TipoRecibo
{
    Vigencia,
    Consolidado,
    PazYSalvo,
}

/// <summary>Estado de la descarga y el almacenamiento de un recibo (plan §1.6).</summary>
public enum EstadoRecibo
{
    Pendiente,
    Guardado,
    SinArchivo,
    PendienteReintentoManual,
}

/// <summary>Un recibo PDF de un ítem: un archivo por <c>fileId</c> distinto, guardado en el file-manager (DA-9c).</summary>
public sealed class Recibo : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ItemId { get; set; }

    public Guid TenantId { get; set; }

    public string FileIdApi { get; set; } = string.Empty;

    public TipoRecibo Tipo { get; set; }

    public string[] Periodos { get; set; } = [];

    public EstadoRecibo Estado { get; set; } = EstadoRecibo.Pendiente;

    public string? ReferenciaAlmacen { get; set; }

    public string? Sha256 { get; set; }

    public long? Tamano { get; set; }

    public int Intentos { get; set; }

    public string? Motivo { get; set; }

    /// <summary>Token de concurrencia optimista (columna de sistema <c>xmin</c>).</summary>
    public uint Xmin { get; set; }
}

/// <summary><c>impuestos.recibos</c> (plan §4.1).</summary>
internal sealed class ReciboConfig : IEntityTypeConfiguration<Recibo>
{
    public void Configure(EntityTypeBuilder<Recibo> builder)
    {
        builder.ToTable("recibos", t =>
        {
            t.HasComment("Recibos PDF de cada ítem (un archivo por fileId, DA-9c).");
            t.HasCheckConstraint("ck_recibos_tipo", $"tipo IN ({CodigosDeEnum.ListaSql<TipoRecibo>()})");
            t.HasCheckConstraint("ck_recibos_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoRecibo>()})");
        });
        builder.HasKey(x => x.Id).HasName("pk_recibos");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<LiquidacionItem>().WithMany().HasForeignKey(x => x.ItemId)
            .HasConstraintName("fk_recibos_liquidacion_items").OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.FileIdApi).HasMaxLength(64);
        builder.Property(x => x.Tipo).HasConversion(CodigosDeEnum.Convertidor<TipoRecibo>()).HasMaxLength(12);
        builder.Property(x => x.Periodos).HasColumnType("varchar(9)[]").HasDefaultValueSql("'{}'").ValueGeneratedNever();
        builder.Property(x => x.Estado).HasConversion(CodigosDeEnum.Convertidor<EstadoRecibo>()).HasMaxLength(28);
        builder.Property(x => x.ReferenciaAlmacen).HasMaxLength(200);
        builder.Property(x => x.Sha256).HasColumnType("char(64)");
        builder.Property(x => x.Intentos).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.Motivo).HasMaxLength(64);
        builder.Property(x => x.Xmin).IsRowVersion();
        builder.HasIndex(x => new { x.ItemId, x.FileIdApi }).IsUnique().HasDatabaseName("ux_recibos_item_file");
    }
}
