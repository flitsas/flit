namespace Flit.Admin.Domain.OtWebhooks;

/// <summary>Tipos de evento soportados para suscripciones webhook OT (HU #10216).</summary>
public static class OtWebhookEventTypes
{
    public const string VehicleStateChanged = "vehicle_state_changed";

    public const string ProcedureStateChanged = "procedure_state_changed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        VehicleStateChanged,
        ProcedureStateChanged,
    };
}
