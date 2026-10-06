using System.Text.RegularExpressions;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>Configuración del bus para un servicio (sección <c>Platform:Messaging</c>).</summary>
public sealed partial class PlatformMessagingOptions
{
    public const string SectionName = "Platform:Messaging";

    /// <summary>URI AMQP del broker del ambiente (p. ej. <c>amqp://flit:***@rabbitmq:5672/</c>).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Código del servicio productor: su exchange es <c>flit.&lt;Producer&gt;</c> (contrato §7).</summary>
    public string Producer { get; set; } = string.Empty;

    /// <summary>Cada cuánto el publicador busca eventos pendientes.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 50;

    internal void Validate()
    {
        if (!ProducerPattern().IsMatch(Producer))
            throw new InvalidOperationException($"{SectionName}:Producer debe ser un código en minúsculas (p. ej. «consultas»).");
        if (!Uri.TryCreate(ConnectionString, UriKind.Absolute, out var uri) || uri.Scheme is not ("amqp" or "amqps"))
            throw new InvalidOperationException($"{SectionName}:ConnectionString debe ser una URI amqp:// o amqps://.");
        if (BatchSize < 1 || PollInterval <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}: BatchSize y PollInterval deben ser positivos.");
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,40}$")]
    private static partial Regex ProducerPattern();
}
