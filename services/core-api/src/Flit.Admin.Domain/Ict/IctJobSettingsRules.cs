namespace Flit.Admin.Domain.Ict;

/// <summary>
/// Clamps del PUT SuperAdmin (HU #12512). La ventana es la misma que
/// <c>IctWindowEvaluator</c>: hora Bogotá inclusiva en el inicio y exclusiva en el fin,
/// sin cruce de medianoche.
/// </summary>
public static class IctJobSettingsRules
{
    public const int HourMin = 0;
    public const int HourMax = 23;
    public const int PollMin = 1;
    public const int PollMax = 3600;
    public const int ConcurrencyMin = 1;
    public const int ConcurrencyMax = 100;
    public const int BatchMin = 1;
    public const int BatchMax = 5000;

    public static IReadOnlyList<IctJobSettingsFieldError> Validate(IctJobSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<IctJobSettingsFieldError>();

        ValidateHour(errors, "windowStartHour", settings.WindowStartHour);
        ValidateHour(errors, "windowEndHour", settings.WindowEndHour);
        if (errors.Count == 0 && settings.WindowStartHour >= settings.WindowEndHour)
        {
            errors.Add(new IctJobSettingsFieldError(
                "windowEndHour",
                "La ventana debe cumplir windowStartHour < windowEndHour (America/Bogota, fin exclusivo)."));
        }

        ValidateRange(errors, "businessPollSeconds", settings.BusinessPollSeconds, PollMin, PollMax);
        ValidateRange(errors, "externalPollSeconds", settings.ExternalPollSeconds, PollMin, PollMax);
        ValidateRange(errors, "orchestratorPollSeconds", settings.OrchestratorPollSeconds, PollMin, PollMax);
        ValidateRange(errors, "sendPollSeconds", settings.SendPollSeconds, PollMin, PollMax);
        ValidateRange(errors, "webhookPollSeconds", settings.WebhookPollSeconds, PollMin, PollMax);

        ValidateRange(errors, "orchestratorConcurrency", settings.OrchestratorConcurrency, ConcurrencyMin, ConcurrencyMax);
        ValidateRange(errors, "sendConcurrency", settings.SendConcurrency, ConcurrencyMin, ConcurrencyMax);

        ValidateRange(errors, "orchestratorBatchSize", settings.OrchestratorBatchSize, BatchMin, BatchMax);
        ValidateRange(errors, "sendBatchSize", settings.SendBatchSize, BatchMin, BatchMax);
        ValidateRange(errors, "webhookBatchSize", settings.WebhookBatchSize, BatchMin, BatchMax);
        ValidateRange(errors, "businessBatchSize", settings.BusinessBatchSize, BatchMin, BatchMax);
        ValidateRange(errors, "externalBatchSize", settings.ExternalBatchSize, BatchMin, BatchMax);

        return errors;
    }

    private static void ValidateHour(List<IctJobSettingsFieldError> errors, string field, int value)
    {
        if (value is < HourMin or > HourMax)
        {
            errors.Add(new IctJobSettingsFieldError(field, $"Debe estar entre {HourMin} y {HourMax}."));
        }
    }

    private static void ValidateRange(
        List<IctJobSettingsFieldError> errors, string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add(new IctJobSettingsFieldError(field, $"Debe estar entre {min} y {max}."));
        }
    }
}

public sealed record IctJobSettingsFieldError(string Field, string Message);
