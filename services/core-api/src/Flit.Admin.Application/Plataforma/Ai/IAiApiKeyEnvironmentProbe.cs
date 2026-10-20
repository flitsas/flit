namespace Flit.Admin.Application.Plataforma.Ai;

/// <summary>Lee presencia de API keys solo desde env / configuración (nunca persiste ni expone el valor).</summary>
public interface IAiApiKeyEnvironmentProbe
{
    bool HasApiKey(string providerCode);

    /// <summary>Solo para construir máscara en memoria; no serializar al cliente.</summary>
    string? ReadKeyForMasking(string providerCode);
}
