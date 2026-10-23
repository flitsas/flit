using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Límites de los casos de soporte de DR. FLIT (<c>DrFlit:SupportCase</c>, HU #12924). Los defaults son los
/// del diseño (ADR-0060 §5.3): 5 adjuntos, 20 MB (el mismo tope de los adjuntos de trámites), imágenes,
/// PDF y texto, 24 h de vida para lo que nunca se confirma.
/// </summary>
public sealed class DrFlitSupportCaseOptions
{
    public const string SectionName = "DrFlit:SupportCase";

    public int MaxAttachments { get; set; } = 5;

    public long MaxFileSizeBytes { get; set; } = 20L * 1024 * 1024;

    public List<string> AllowedMimeTypes { get; set; } =
        ["image/png", "image/jpeg", "image/webp", "application/pdf", "text/plain"];

    public int AttachmentTtlHours { get; set; } = 24;

    /// <summary>
    /// <c>DrFlit:DeployEnvironment</c> / env <c>DR_FLIT_DEPLOY_ENVIRONMENT</c> (DEV|QA|PDN). Se lee aparte
    /// de <c>ASPNETCORE_ENVIRONMENT</c> porque los tres VPS corren en Development (ADR-0060 §9.3).
    /// </summary>
    public string? DeployEnvironment { get; set; }
}

internal sealed class DrFlitSupportCaseSettings(IOptions<DrFlitSupportCaseOptions> options) : IDrFlitSupportCaseSettings
{
    private readonly DrFlitSupportCaseOptions _options = options.Value;

    public int MaxAttachments => _options.MaxAttachments;

    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    public IReadOnlyCollection<string> AllowedMimeTypes => _options.AllowedMimeTypes;

    public TimeSpan AttachmentTtl => TimeSpan.FromHours(Math.Max(1, _options.AttachmentTtlHours));

    public DrFlitDeployEnvironment DeployEnvironment => DrFlitSupportCaseWire.ParseDeployEnvironment(_options.DeployEnvironment);
}
