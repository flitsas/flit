namespace Flit.Modules.Quipux.Domain.Trazabilidad;

/// <summary>
/// Ejecución de un worker Quipux (<c>admin.quipux_job_runs</c>). Complementa a
/// <see cref="QuipuxSubmissionEvent"/>: aquel traza el trámite, este traza el job. Responde
/// "¿corrió el cron?", "¿cuánto tomó?", "¿cuántos falló?" — que en FLIT 1.0 no se podía saber
/// porque el scheduler era una Lambda sin bitácora.
/// </summary>
public sealed class QuipuxJobRun
{
    public Guid Id { get; set; }

    /// <summary>Ver <see cref="QuipuxJobNames"/>.</summary>
    public string JobName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null mientras corre. Una fila vieja con esto en null delata un worker que murió a medio ciclo.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public int ClaimedCount { get; set; }

    public int OkCount { get; set; }

    public int ErrorCount { get; set; }

    /// <summary>Error que abortó el ciclo entero (no el de un elemento: ese va al evento de la submission).</summary>
    public string? ErrorMessage { get; set; }
}

public static class QuipuxJobNames
{
    public const string Register = "quipux_register";

    public const string StatusPoll = "quipux_status_poll";
}
