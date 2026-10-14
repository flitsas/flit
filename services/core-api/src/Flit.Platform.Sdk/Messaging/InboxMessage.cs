using Microsoft.EntityFrameworkCore;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>
/// Un evento ya procesado por un consumidor (HU #13339, ADR-0064): llave <c>(event_id, consumer)</c> en el esquema del
/// servicio. Se guarda en la misma transacción que el efecto: si el evento vuelve a llegar, se confirma sin repetirlo.
/// </summary>
public sealed class InboxMessage
{
    public Guid EventId { get; set; }

    /// <summary>Nombre de la cola del consumidor (<c>&lt;consumidor&gt;.&lt;propósito&gt;</c>).</summary>
    public string Consumer { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}

public static class InboxModelBuilderExtensions
{
    /// <summary>Tabla <c>&lt;schema&gt;.inbox</c>, con nombres de columna explícitos.</summary>
    public static ModelBuilder AddFlitInbox(this ModelBuilder modelBuilder, string schema)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.ToTable("inbox", schema);
            e.HasKey(m => new { m.EventId, m.Consumer }).HasName("pk_inbox");
            e.Property(m => m.EventId).HasColumnName("event_id");
            e.Property(m => m.Consumer).HasColumnName("consumer").HasMaxLength(200);
            e.Property(m => m.Type).HasColumnName("type").HasMaxLength(200).IsRequired();
            e.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        });
        return modelBuilder;
    }
}
