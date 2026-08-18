using System.Text.RegularExpressions;

namespace Flit.Analytics.Application.Scheduling;

/// <summary>
/// Validación de los payloads de programación de informes y reglas de alerta (HU-D §4.7).
/// Devuelve mensajes de negocio EN ESPAÑOL (contrato: 400 con detalle en español) o el
/// modelo normalizado con defaults aplicados. Sin I/O: apta para tests unitarios puros.
/// </summary>
public static partial class SchedulingValidation
{
    public const int MaxNameLength = 120;
    public const int MinRecipients = 1;
    public const int MaxRecipients = 10;
    public const int DefaultSendHour = 7;
    public const int DefaultWindowMinutes = 1440;
    public const int DefaultCooldownMinutes = 240;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 43_200;
    public const int MinCooldownMinutes = 5;
    public const int MaxCooldownMinutes = 10_080;

    private static readonly string[] ReportTypes =
        ["resumen", "operacion", "ot", "uso", "productividad", "consulta"];
    private static readonly string[] SavedQueryScopes = ["empresa", "superadmin"];
    private static readonly string[] Frequencies = ["daily", "weekly", "monthly"];
    private static readonly string[] Formats = ["excel", "pdf"];
    private static readonly string[] Metrics =
    [
        "rejection_rate_pct", "stuck_count", "external_api_errors", "pending_identity_validations",
        // Métricas ICT (HU5 / E1) evaluadas cross-schema sobre ict.*
        "ict_stuck_in_validation", "ict_novelty_rate_pct", "ict_webhook_delivery_failures", "ict_jobs_out_of_sla",
    ];
    private static readonly string[] Operators = ["gt", "gte", "lt", "lte"];

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailRegex();

    /// <summary>Valida y normaliza un informe programado. Error = mensaje en español; null si es válido.</summary>
    public static (ValidatedReportSchedule? Result, string? Error) ValidateReportSchedule(ReportScheduleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (null, "El nombre del informe es obligatorio.");
        if (name.Length > MaxNameLength)
            return (null, $"El nombre no puede superar los {MaxNameLength} caracteres.");

        if (input.ReportType is null || !ReportTypes.Contains(input.ReportType))
            return (null, "El tipo de informe debe ser uno de: resumen, operacion, ot, uso, productividad, consulta.");

        // Reportes 2.0 (HU-D, segunda ola) — un informe de tipo "consulta" apunta a una SavedQuery
        // en vez de a uno de los 5 tipos agregados; espejo del CHECK
        // report_schedules_consulta_shape_check (§75 del DDL).
        if (input.ReportType == "consulta")
        {
            if (input.SavedQueryId is null)
                return (null, "Un informe de tipo 'consulta' requiere indicar savedQueryId.");
            if (input.SavedQueryScope is null || !SavedQueryScopes.Contains(input.SavedQueryScope))
                return (null, "El alcance de la consulta debe ser 'empresa' o 'superadmin'.");
            if (input.Format is not null && input.Format != "excel")
                return (null, "Un informe de tipo 'consulta' solo se entrega en formato Excel.");
        }
        else if (input.SavedQueryId is not null || input.SavedQueryScope is not null)
        {
            return (null, "savedQueryId y el alcance de la consulta solo aplican a informes de tipo 'consulta'.");
        }

        if (input.Frequency is null || !Frequencies.Contains(input.Frequency))
            return (null, "La periodicidad debe ser una de: daily, weekly, monthly.");

        switch (input.Frequency)
        {
            case "daily":
                if (input.DayOfWeek is not null || input.DayOfMonth is not null)
                    return (null, "Un informe diario no debe indicar día de la semana ni día del mes.");
                break;
            case "weekly":
                if (input.DayOfWeek is not (>= 0 and <= 6))
                    return (null, "Un informe semanal requiere un día de la semana entre 0 (domingo) y 6 (sábado).");
                if (input.DayOfMonth is not null)
                    return (null, "Un informe semanal no debe indicar día del mes.");
                break;
            case "monthly":
                if (input.DayOfMonth is not (>= 1 and <= 28))
                    return (null, "Un informe mensual requiere un día del mes entre 1 y 28.");
                if (input.DayOfWeek is not null)
                    return (null, "Un informe mensual no debe indicar día de la semana.");
                break;
        }

        var sendHour = input.SendHour ?? DefaultSendHour;
        if (sendHour is < 0 or > 23)
            return (null, "La hora de envío debe estar entre 0 y 23 (hora de Bogotá).");

        if (input.Format is null || !Formats.Contains(input.Format))
            return (null, "El formato debe ser 'excel' o 'pdf'.");

        var (recipients, emailError) = ValidateRecipients(input.Recipients);
        if (emailError is not null)
            return (null, emailError);

        return (new ValidatedReportSchedule(
            name, input.ReportType, input.Frequency, input.DayOfWeek, input.DayOfMonth,
            sendHour, input.Format, recipients!, input.IsActive ?? true,
            input.SavedQueryId, input.SavedQueryScope), null);
    }

    /// <summary>Valida y normaliza una regla de alerta. Error = mensaje en español; null si es válida.</summary>
    public static (ValidatedAlertRule? Result, string? Error) ValidateAlertRule(AlertRuleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (null, "El nombre de la alerta es obligatorio.");
        if (name.Length > MaxNameLength)
            return (null, $"El nombre no puede superar los {MaxNameLength} caracteres.");

        if (input.Metric is null || !Metrics.Contains(input.Metric))
            return (null, "La métrica debe ser una de: rejection_rate_pct, stuck_count, external_api_errors, " +
                "pending_identity_validations, ict_stuck_in_validation, ict_novelty_rate_pct, " +
                "ict_webhook_delivery_failures, ict_jobs_out_of_sla.");

        if (input.Operator is null || !Operators.Contains(input.Operator))
            return (null, "El operador debe ser uno de: gt, gte, lt, lte.");

        if (input.Threshold is null)
            return (null, "El umbral es obligatorio.");

        var window = input.WindowMinutes ?? DefaultWindowMinutes;
        if (window is < MinWindowMinutes or > MaxWindowMinutes)
            return (null, $"La ventana de evaluación debe estar entre {MinWindowMinutes} y {MaxWindowMinutes} minutos.");

        var cooldown = input.CooldownMinutes ?? DefaultCooldownMinutes;
        if (cooldown is < MinCooldownMinutes or > MaxCooldownMinutes)
            return (null, $"El periodo de enfriamiento debe estar entre {MinCooldownMinutes} y {MaxCooldownMinutes} minutos.");

        var (recipients, emailError) = ValidateRecipients(input.Recipients);
        if (emailError is not null)
            return (null, emailError);

        return (new ValidatedAlertRule(
            name, input.Metric, input.Operator, input.Threshold.Value,
            window, cooldown, recipients!, input.IsActive ?? true), null);
    }

    /// <summary>1..10 correos válidos, sin duplicados (case-insensitive) y sin espacios.</summary>
    private static (IReadOnlyList<string>? Recipients, string? Error) ValidateRecipients(
        IReadOnlyList<string>? recipients)
    {
        var cleaned = (recipients ?? [])
            .Select(r => r?.Trim() ?? string.Empty)
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count < MinRecipients)
            return (null, "Debe indicar al menos un destinatario de correo.");
        if (cleaned.Count > MaxRecipients)
            return (null, $"No puede indicar más de {MaxRecipients} destinatarios.");

        var invalid = cleaned.FirstOrDefault(r => !EmailRegex().IsMatch(r));
        if (invalid is not null)
            return (null, $"El correo '{invalid}' no es una dirección válida.");

        return (cleaned, null);
    }
}
