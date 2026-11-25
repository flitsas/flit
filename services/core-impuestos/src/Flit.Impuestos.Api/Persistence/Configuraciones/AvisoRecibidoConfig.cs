using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Dedupe de los avisos del API por <c>X-Flit-Delivery</c>. Global: un token desconocido no es de ninguna compañía, así que
/// <see cref="TenantId"/> es opcional e informativo. Sin cuerpo (puede traer PII). Se purga a los 30 días.
/// </summary>
public sealed class AvisoRecibido
{
    public string DeliveryId { get; set; } = string.Empty;

    public Guid? ItemId { get; set; }

    public Guid? TenantId { get; set; }

    public string? ConsultaId { get; set; }

    public int? Attempt { get; set; }

    public DateTimeOffset RecibidoEn { get; set; }

    public string Resultado { get; set; } = string.Empty;
}

/// <summary><c>impuestos.avisos_recibidos</c> (plan §4.1).</summary>
internal sealed class AvisoRecibidoConfig : IEntityTypeConfiguration<AvisoRecibido>
{
    public void Configure(EntityTypeBuilder<AvisoRecibido> builder)
    {
        builder.ToTable("avisos_recibidos", t => t.HasComment("Dedupe de avisos por X-Flit-Delivery; sin cuerpo (PII). Purga a los 30 días. Global."));
        builder.HasKey(x => x.DeliveryId).HasName("pk_avisos_recibidos");
        builder.Property(x => x.DeliveryId).HasMaxLength(80).ValueGeneratedNever();
        builder.Property(x => x.ConsultaId).HasMaxLength(64);
        builder.Property(x => x.Resultado).HasMaxLength(24);
        builder.HasIndex(x => x.RecibidoEn).HasDatabaseName("ix_avisos_recibidos_recibido_en");
    }
}
