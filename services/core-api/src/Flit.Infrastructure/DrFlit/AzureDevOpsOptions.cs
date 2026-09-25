namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Conexión a Azure DevOps para radicar casos de soporte de DR. FLIT (HU #12923, ADR-0060). Sección
/// <c>DrFlit:AzureDevOps</c> con fallback a env <c>DR_FLIT_ADO_*</c>. El PAT es de la cuenta de servicio
/// "Dr. FLIT" (Work Items R/W solo en el proyecto de soporte): nunca en el repo, nunca en logs.
/// </summary>
public sealed class AzureDevOpsOptions
{
    public const string SectionName = "DrFlit:AzureDevOps";

    /// <summary>URL de la organización, sin el proyecto (p. ej. <c>https://dev.azure.com/FlitDevOps</c>).</summary>
    public string OrganizationUrl { get; set; } = "https://dev.azure.com/FlitDevOps";

    /// <summary>Proyecto donde se crean los Bugs. Tiene espacios: se codifica al armar la URL.</summary>
    public string Project { get; set; } = "FLIT - SOPORTE";

    public string Pat { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Prefijo del título del Bug; el formulario web usa <c>[ Formulario ]</c>.</summary>
    public string TitlePrefix { get; set; } = "[ DR. FLIT ]";
}

/// <summary>
/// Mapeo de los datos del caso a los campos del Bug (HU #12923 AC1/AC4). Todo configurable en
/// <c>DrFlit:SupportCase:FieldMapping</c>: si el proceso de ADO cambia un picklist, se ajusta aquí sin
/// tocar el cliente. Los defaults reflejan los valores permitidos hoy en <c>FLIT - SOPORTE</c>.
/// </summary>
public sealed class DrFlitFieldMappingOptions
{
    public const string SectionName = "DrFlit:SupportCase:FieldMapping";

    /// <summary>Prioridad del usuario → <c>Custom.Primacy</c>.</summary>
    public Dictionary<string, string> Primacy { get; set; } = new(StringComparer.Ordinal)
    {
        ["Alta"] = "1",
        ["Media"] = "2",
        ["Baja"] = "3",
    };

    /// <summary>Prioridad del usuario → <c>Microsoft.VSTS.Common.Severity</c>.</summary>
    public Dictionary<string, string> Severity { get; set; } = new(StringComparer.Ordinal)
    {
        ["Alta"] = "2 - High",
        ["Media"] = "3 - Medium",
        ["Baja"] = "4 - Low",
    };

    /// <summary>Frecuencia (<c>una_vez</c>/<c>a_veces</c>/<c>siempre</c>) → <c>Custom.Incidence</c>.</summary>
    public Dictionary<string, string> Incidence { get; set; } = new(StringComparer.Ordinal)
    {
        ["una_vez"] = "1",
        ["a_veces"] = "2",
        ["siempre"] = "3+",
    };

    /// <summary>Ambiente de despliegue (DEV/QA/PDN) → <c>Custom.Environment</c>.</summary>
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal)
    {
        ["DEV"] = "DEV",
        ["QA"] = "QA",
        ["PDN"] = "PDN",
    };

    /// <summary>Valores aceptados de <c>Custom.AffectedModule</c> (picklist del proyecto de soporte).</summary>
    public List<string> AffectedModules { get; set; } =
    [
        "Administradores", "Consulta Externos", "Correcol", "Diagnostico", "Fasecolda", "GDC",
        "Generador PDF", "ICT", "Impuesto", "Login", "Matricula", "Notificacion", "OCR",
        "Otros Tramites", "Qx", "Reportes", "Traspasos", "Usuarios", "Validación de Identidad",
    ];

    /// <summary>Módulo cuando el frontend no envía uno o envía uno fuera del allow-list.</summary>
    public string DefaultAffectedModule { get; set; } = "Otros Tramites";

    /// <summary><c>Custom.TypeBug</c> inicial; soporte lo reclasifica al hacer triage.</summary>
    public string TypeBug { get; set; } = "Sin Definir";
}
