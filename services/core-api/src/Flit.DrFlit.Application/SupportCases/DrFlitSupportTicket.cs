namespace Flit.DrFlit.Application.SupportCases;

/// <summary>Prioridad que elige el usuario en el formulario (ADR-0060). Se mapea a Primacy y Severity.</summary>
public enum DrFlitCasePriority
{
    Alta,
    Media,
    Baja,
}

/// <summary>Frecuencia del error que reporta el usuario. Se mapea a <c>Custom.Incidence</c>.</summary>
public enum DrFlitCaseFrequency
{
    UnaVez,
    AVeces,
    Siempre,
}

/// <summary>
/// Ambiente donde corre este backend. NO se infiere de <c>ASPNETCORE_ENVIRONMENT</c>: los tres VPS corren
/// en Development (ADR-0060 §9.3). Sale de <c>DrFlit:DeployEnvironment</c> / <c>DR_FLIT_DEPLOY_ENVIRONMENT</c>.
/// </summary>
public enum DrFlitDeployEnvironment
{
    DEV,
    QA,
    PDN,
}

/// <summary>Valores de los enums en el contrato JSON del formulario.</summary>
public static class DrFlitSupportCaseWire
{
    public static bool TryParsePriority(string? value, out DrFlitCasePriority priority)
    {
        switch (value)
        {
            case "Alta": priority = DrFlitCasePriority.Alta; return true;
            case "Media": priority = DrFlitCasePriority.Media; return true;
            case "Baja": priority = DrFlitCasePriority.Baja; return true;
            default: priority = default; return false;
        }
    }

    public static bool TryParseFrequency(string? value, out DrFlitCaseFrequency frequency)
    {
        switch (value)
        {
            case "una_vez": frequency = DrFlitCaseFrequency.UnaVez; return true;
            case "a_veces": frequency = DrFlitCaseFrequency.AVeces; return true;
            case "siempre": frequency = DrFlitCaseFrequency.Siempre; return true;
            default: frequency = default; return false;
        }
    }

    public static string ToWire(this DrFlitCaseFrequency frequency) => frequency switch
    {
        DrFlitCaseFrequency.UnaVez => "una_vez",
        DrFlitCaseFrequency.AVeces => "a_veces",
        DrFlitCaseFrequency.Siempre => "siempre",
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null),
    };

    /// <summary>
    /// Ambiente de despliegue desde la configuración. Cualquier valor ausente o desconocido cae en
    /// <see cref="DrFlitDeployEnvironment.DEV"/>: el ambiente menos sensible, nunca PDN por omisión.
    /// </summary>
    public static DrFlitDeployEnvironment ParseDeployEnvironment(string? value) =>
        Enum.TryParse<DrFlitDeployEnvironment>(value?.Trim(), ignoreCase: true, out var env) && Enum.IsDefined(env)
            ? env
            : DrFlitDeployEnvironment.DEV;
}

/// <summary>
/// Caso de soporte ya confirmado por el usuario, listo para radicar. Ningún campo viene del LLM
/// (ADR-0060 §8.2.6): todo sale del formulario estructurado, del token o de la configuración.
/// </summary>
/// <param name="AffectedModule">Mejor esfuerzo del frontend; el gateway lo valida contra su allow-list.</param>
public sealed record DrFlitSupportTicket(
    string Title,
    string RequesterName,
    string RequesterEmail,
    string? RequesterPhone,
    string Company,
    string Detail,
    string ExpectedResult,
    DrFlitCaseFrequency Frequency,
    DrFlitCasePriority Priority,
    DrFlitDeployEnvironment Environment,
    string? AffectedModule,
    DateTimeOffset ReportedAt);

/// <summary>Adjunto a subir junto con el caso. El contenido se abre solo al momento de subirlo.</summary>
public sealed record DrFlitTicketAttachment(
    string FileName,
    string ContentType,
    Func<CancellationToken, Task<Stream?>> OpenAsync);

/// <summary>Resultado de radicar el caso en el sistema de soporte.</summary>
/// <param name="ErrorCode">Tipo de fallo, sin PII (p. ej. <c>http_503</c>, <c>timeout</c>, <c>not_configured</c>).</param>
public sealed record DrFlitBugCreationResult(
    bool Created,
    int? WorkItemId,
    string? WorkItemUrl,
    int AttachmentsFailed,
    string? ErrorCode)
{
    public static DrFlitBugCreationResult Failed(string errorCode, int attachmentsFailed = 0) =>
        new(false, null, null, attachmentsFailed, errorCode);
}
