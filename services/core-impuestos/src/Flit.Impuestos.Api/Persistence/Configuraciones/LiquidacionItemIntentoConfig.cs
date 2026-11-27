using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Un intento cerrado de un ítem (R3, decisión del usuario 2026-11-26): al relanzar, la foto del intento que termina (número,
/// estado, error, campo faltante y total a pagar), cuándo se cerró y quién relanzó. El intento en curso vive en el propio ítem
/// (<see cref="LiquidacionItem.NumeroIntento"/>). Sin PII del propietario y sin el resultado cifrado: solo códigos, montos y el
/// nombre del usuario interno que relanzó.
/// </summary>
/// <example>
/// <code>
/// db.Add(LiquidacionItemIntento.De(item, ahora, "operador@empresa.test")); // numero = item.NumeroIntento
/// </code>
/// </example>
public sealed class LiquidacionItemIntento : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ItemId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Número del intento cerrado (1..n), el que tenía el ítem al relanzar.</summary>
    public int Numero { get; set; }

    public EstadoItem Estado { get; set; }

    public string? ErrorCodigo { get; set; }

    public ClaseError? ErrorClase { get; set; }

    public string? ErrorMensaje { get; set; }

    public string? CampoFaltante { get; set; }

    /// <summary>Total a pagar del intento; null no es 0 (F2-CF14).</summary>
    public decimal? TotalAPagar { get; set; }

    /// <summary>Cuándo se cerró el intento (el momento del relanzamiento).</summary>
    public DateTimeOffset RegistradoEn { get; set; }

    /// <summary>Nombre de quien relanzó, tal como venía en su token.</summary>
    public string? RelanzadoPor { get; set; }

    /// <summary>La foto del intento en curso de <paramref name="item"/>, cerrado en <paramref name="ahora"/> por <paramref name="relanzadoPor"/>.</summary>
    public static LiquidacionItemIntento De(LiquidacionItem item, DateTimeOffset ahora, string? relanzadoPor)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new LiquidacionItemIntento
        {
            ItemId = item.Id,
            TenantId = item.TenantId,
            Numero = item.NumeroIntento,
            Estado = item.Estado,
            ErrorCodigo = item.ErrorCodigo,
            ErrorClase = item.ErrorClase,
            ErrorMensaje = item.ErrorMensaje,
            CampoFaltante = item.CampoFaltante,
            TotalAPagar = item.TotalAPagar,
            RegistradoEn = ahora,
            RelanzadoPor = relanzadoPor,
        };
    }
}

/// <summary><c>impuestos.liquidacion_item_intentos</c> (R3): mismas convenciones que <c>liquidacion_items</c> (empresa, snake_case, CHECK de códigos).</summary>
internal sealed class LiquidacionItemIntentoConfig : IEntityTypeConfiguration<LiquidacionItemIntento>
{
    public void Configure(EntityTypeBuilder<LiquidacionItemIntento> builder)
    {
        builder.ToTable("liquidacion_item_intentos", t =>
        {
            t.HasComment("Intentos cerrados de cada ítem al relanzarlo (R3). Sin PII del propietario ni resultado cifrado.");
            t.HasCheckConstraint("ck_liquidacion_item_intentos_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoItem>()})");
            t.HasCheckConstraint("ck_liquidacion_item_intentos_error_clase", $"error_clase IS NULL OR error_clase IN ({CodigosDeEnum.ListaSql<ClaseError>()})");
            t.HasCheckConstraint("ck_liquidacion_item_intentos_numero", "numero >= 1");
        });
        builder.HasKey(x => x.Id).HasName("pk_liquidacion_item_intentos");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<LiquidacionItem>().WithMany().HasForeignKey(x => x.ItemId)
            .HasConstraintName("fk_liquidacion_item_intentos_liquidacion_items").OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Estado).HasConversion(CodigosDeEnum.Convertidor<EstadoItem>()).HasMaxLength(32);
        builder.Property(x => x.ErrorCodigo).HasMaxLength(48);
        builder.Property(x => x.ErrorClase).HasConversion(CodigosDeEnum.Convertidor<ClaseError>()).HasMaxLength(16);
        builder.Property(x => x.ErrorMensaje).HasMaxLength(500);
        builder.Property(x => x.CampoFaltante).HasMaxLength(64);
        builder.Property(x => x.TotalAPagar).HasPrecision(18, 2).HasComment("NULL no es 0 (F2-CF14).");
        builder.Property(x => x.RelanzadoPor).HasMaxLength(200).HasComment(ComentariosPii.NombreUsuarioInterno);
        // Índice por item_id (lectura del detalle, más reciente primero) que además impide repetir un número de intento.
        builder.HasIndex(x => new { x.ItemId, x.Numero }).IsUnique().HasDatabaseName("ux_liquidacion_item_intentos_item_numero");
    }
}
