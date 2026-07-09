namespace Flit.Infrastructure.Telemetry;

/// <summary>
/// Taxonomía CERRADA de eventos de telemetría (contrato Reportes 2.0 §7). El endpoint
/// batch descarta silenciosamente cualquier <c>event_type</c> fuera de esta lista.
/// </summary>
public static class UsageEventTypes
{
    public const string ModuleView = "module_view";
    public const string ApiModuleAccess = "api_module_access";
    public const string WizardServerView = "wizard_server_view";
    public const string WizardStepView = "wizard_step_view";
    public const string WizardStepComplete = "wizard_step_complete";
    public const string WizardStepExit = "wizard_step_exit";
    public const string WizardAbandon = "wizard_abandon";
    public const string WizardComplete = "wizard_complete";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        ModuleView,
        ApiModuleAccess,
        WizardServerView,
        WizardStepView,
        WizardStepComplete,
        WizardStepExit,
        WizardAbandon,
        WizardComplete,
    };

    /// <summary>¿El tipo pertenece a la taxonomía cerrada?</summary>
    public static bool IsKnown(string? eventType) =>
        !string.IsNullOrWhiteSpace(eventType) && Known.Contains(eventType);
}
