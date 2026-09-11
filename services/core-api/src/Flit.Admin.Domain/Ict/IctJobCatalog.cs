namespace Flit.Admin.Domain.Ict;

/// <summary>
/// Catálogo estático de procesos periódicos ICT (HU #12513).
/// Las claves coinciden con <c>ict.job_runs.job_name</c> del pipeline.
/// Retention no escribe job_runs.
/// </summary>
public static class IctJobCatalog
{
    public const string Owner = "core-ict";

    public const string OrchestratorRuntNote =
        "Consultas RUNT/familia del pre-trámite vía proveedor configurado (Orchestrator). "
        + "No es Lambda/EventBridge ni Confirmación RUNT post-aprobación "
        + "(/admin/plataforma/confirmacion-runt).";

    public static IReadOnlyList<IctJobDefinition> All { get; } =
    [
        new(
            "business-validation",
            "BusinessValidation",
            [IctJobWorkTypes.Bd],
            HasPipelineRuns: true,
            Notes: null),
        new(
            "external-validation",
            "ExternalValidation",
            [IctJobWorkTypes.Bd],
            HasPipelineRuns: true,
            Notes: null),
        new(
            "orchestrator",
            "Orchestrator",
            [IctJobWorkTypes.Bd, IctJobWorkTypes.EndpointInterno, IctJobWorkTypes.EndpointExterno],
            HasPipelineRuns: true,
            Notes: OrchestratorRuntNote),
        new(
            "send-to-core-api",
            "SendToCoreApi",
            [IctJobWorkTypes.Bd, IctJobWorkTypes.EndpointInterno],
            HasPipelineRuns: true,
            Notes: null),
        new(
            "webhook-notification",
            "WebhookNotification",
            [IctJobWorkTypes.Bd, IctJobWorkTypes.EndpointExterno],
            HasPipelineRuns: true,
            Notes: null),
        new(
            "retention",
            "Retention",
            [IctJobWorkTypes.Bd],
            HasPipelineRuns: false,
            Notes: "Purga de observabilidad (integration_log / job_runs / pretramite_events). No registra ciclos en ict.job_runs."),
    ];

    public static IctJobDefinition? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var job in All)
        {
            if (string.Equals(job.Key, key, StringComparison.Ordinal))
            {
                return job;
            }
        }

        return null;
    }
}
