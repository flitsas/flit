using Microsoft.EntityFrameworkCore;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>
/// Un evento por publicar, guardado en la base del servicio en la misma transacción que el cambio que lo origina
/// (HU #13338, ADR-0064). El publicador lo saca de aquí, lo manda al broker y lo sella al recibir la confirmación.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>= <see cref="EventEnvelope.EventId"/>.</summary>
    public Guid Id { get; set; }

    public string Exchange { get; set; } = string.Empty;

    /// <summary>= <see cref="EventEnvelope.Type"/> (el exchange es topic: la llave de ruteo es el tipo).</summary>
    public string RoutingKey { get; set; } = string.Empty;

    /// <summary>El sobre completo en JSON, tal como viaja.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Intentos fallidos de publicación. Sin tope: un broker caído es transitorio y el evento no se pierde.</summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Tabla <c>&lt;schema&gt;.outbox</c> en el esquema propio del servicio. Nombres de columna explícitos, para que no
    /// dependan de la convención del <c>DbContext</c>.
    /// </summary>
    public static ModelBuilder AddFlitOutbox(this ModelBuilder modelBuilder, string schema)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox", schema);
            e.HasKey(m => m.Id).HasName("pk_outbox");
            e.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(m => m.Exchange).HasColumnName("exchange").HasMaxLength(100).IsRequired();
            e.Property(m => m.RoutingKey).HasColumnName("routing_key").HasMaxLength(200).IsRequired();
            e.Property(m => m.Payload).HasColumnName("payload").IsRequired();
            e.Property(m => m.OccurredAt).HasColumnName("occurred_at");
            e.Property(m => m.PublishedAt).HasColumnName("published_at");
            e.Property(m => m.Attempts).HasColumnName("attempts");
            e.Property(m => m.LastError).HasColumnName("last_error").HasMaxLength(2000);
            e.HasIndex(m => m.OccurredAt).HasDatabaseName("ix_outbox_pendientes").HasFilter("published_at IS NULL");
        });
        return modelBuilder;
    }
}
