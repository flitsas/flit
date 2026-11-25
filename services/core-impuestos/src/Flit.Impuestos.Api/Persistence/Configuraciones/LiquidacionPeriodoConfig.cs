using Flit.Impuestos.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>Una vigencia liquidada de un ítem, con sus montos (null no es 0) y la referencia de su recibo en el API.</summary>
public sealed class LiquidacionPeriodo : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ItemId { get; set; }

    public Guid TenantId { get; set; }

    public string Periodo { get; set; } = string.Empty;

    public decimal? Avaluo { get; set; }

    public decimal? Impuesto { get; set; }

    public decimal? Sancion { get; set; }

    public decimal? Intereses { get; set; }

    public decimal? Descuentos { get; set; }

    public decimal? PagosPrevios { get; set; }

    public decimal? TotalAPagar { get; set; }

    /// <summary>jsonb con los otros cargos de la vigencia (arreglo).</summary>
    public string OtrosCargos { get; set; } = "[]";

    public DateOnly? Vencimiento { get; set; }

    public string? ReferenciaPago { get; set; }

    public string? FileIdApi { get; set; }

    public bool NoLiquidable { get; set; }
}

/// <summary><c>impuestos.liquidacion_periodos</c> (plan §4.1).</summary>
internal sealed class LiquidacionPeriodoConfig : IEntityTypeConfiguration<LiquidacionPeriodo>
{
    public void Configure(EntityTypeBuilder<LiquidacionPeriodo> builder)
    {
        builder.ToTable("liquidacion_periodos", t => t.HasComment("Vigencias liquidadas de cada ítem. Sin PII."));
        builder.HasKey(x => x.Id).HasName("pk_liquidacion_periodos");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<LiquidacionItem>().WithMany().HasForeignKey(x => x.ItemId)
            .HasConstraintName("fk_liquidacion_periodos_liquidacion_items").OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Periodo).HasMaxLength(9);
        foreach (var monto in new[]
                 {
                     nameof(LiquidacionPeriodo.Avaluo), nameof(LiquidacionPeriodo.Impuesto), nameof(LiquidacionPeriodo.Sancion),
                     nameof(LiquidacionPeriodo.Intereses), nameof(LiquidacionPeriodo.Descuentos), nameof(LiquidacionPeriodo.PagosPrevios),
                     nameof(LiquidacionPeriodo.TotalAPagar),
                 })
        {
            builder.Property<decimal?>(monto).HasPrecision(18, 2);
        }

        builder.Property(x => x.OtrosCargos).HasColumnType("jsonb").HasDefaultValueSql("'[]'").ValueGeneratedNever();
        builder.Property(x => x.ReferenciaPago).HasMaxLength(80);
        builder.Property(x => x.FileIdApi).HasMaxLength(64);
        builder.Property(x => x.NoLiquidable).HasDefaultValue(false).ValueGeneratedNever();
        builder.HasIndex(x => x.ItemId).HasDatabaseName("ix_liquidacion_periodos_item");
    }
}
