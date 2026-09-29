using Flit.DrFlit.Application.Chat;
using Flit.DrFlit.Application.Manual;

namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Fuente del manual que usa DR. FLIT como contexto del LLM (ADR-0060 §3). Hoy es el artefacto JSON
/// generado desde <c>frontend/lib/manual</c> (HU #12921); cambiar de fuente no toca este contrato.
/// </summary>
public interface IDrFlitManualCatalogProvider
{
    /// <summary>
    /// Catálogo cargado, o <c>null</c> si no está disponible (artefacto ausente o corrupto). Sin
    /// catálogo el chat responde en modo degradado: no hay con qué responder ni cómo validar citas.
    /// </summary>
    DrFlitManualCatalog? GetCatalog();
}

/// <summary>Manual listo para usar: artículos por slug y el <c>system</c> ya armado.</summary>
public sealed class DrFlitManualCatalog
{
    public DrFlitManualCatalog(IReadOnlyList<DrFlitManualArticle> articles)
    {
        ArgumentNullException.ThrowIfNull(articles);

        Articles = articles;
        BySlug = articles
            .GroupBy(a => a.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        // Se arma una sola vez: el bloque del manual tiene que ser idéntico entre llamadas para que la
        // caché de Anthropic lo reconozca.
        SystemPrompt = DrFlitPromptBuilder.Build(articles);
    }

    public IReadOnlyList<DrFlitManualArticle> Articles { get; }

    public IReadOnlyDictionary<string, DrFlitManualArticle> BySlug { get; }

    public DrFlitSystemPrompt SystemPrompt { get; }
}
