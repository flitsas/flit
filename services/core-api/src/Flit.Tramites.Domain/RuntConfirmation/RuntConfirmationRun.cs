namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// Bitácora de una corrida (<c>tramites.runt_confirmation_runs</c>, HU #12309). Responde «¿corrió?»,
/// «¿cuántos consultó?», «¿cuántas llamadas pagó?» — el patrón de <c>admin.quipux_job_runs</c>. Una fila
/// con <c>finished_at</c> NULL y <c>started_at</c> viejo delata una corrida que murió a medias.
/// </summary>
public sealed class RuntConfirmationRun
{
    public Guid Id { get; set; }

    /// <summary><see cref="RuntConfirmationRunTriggers"/>: programada, manual (Consultar ahora) o forzada.</summary>
    public string Trigger { get; set; } = RuntConfirmationRunTriggers.Scheduled;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Proveedor con el que se consultó. NULL si la corrida se saltó.</summary>
    public string? ProviderKey { get; set; }

    /// <summary><see cref="RuntConfirmationSkipReasons"/> cuando no se consultó nada.</summary>
    public string? SkippedReason { get; set; }

    public int Consulted { get; set; }
    public int Confirmed { get; set; }
    public int Pending { get; set; }
    public int Discrepancies { get; set; }
    public int Unverifiable { get; set; }
    public int Errors { get; set; }

    /// <summary>Llamadas reales al proveedor (un traspaso cuenta dos).</summary>
    public int ProviderCalls { get; set; }

    /// <summary>Solo el error que abortó la corrida entera; el de un trámite va en su intento.</summary>
    public string? ErrorMessage { get; set; }
}

public static class RuntConfirmationRunTriggers
{
    public const string Scheduled = "scheduled";
    public const string Manual = "manual";

    public static readonly IReadOnlyList<string> All = [Scheduled, Manual];
}

public static class RuntConfirmationSkipReasons
{
    public const string Disabled = "disabled";
    public const string AlreadyRunning = "already_running";

    public static readonly IReadOnlyList<string> All = [Disabled, AlreadyRunning];
}
