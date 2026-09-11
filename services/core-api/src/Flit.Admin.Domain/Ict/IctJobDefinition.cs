namespace Flit.Admin.Domain.Ict;

/// <summary>Metadatos estáticos de un job ICT (sin bitácora).</summary>
public sealed record IctJobDefinition(
    string Key,
    string DisplayName,
    IReadOnlyList<string> Types,
    bool HasPipelineRuns,
    string? Notes);
