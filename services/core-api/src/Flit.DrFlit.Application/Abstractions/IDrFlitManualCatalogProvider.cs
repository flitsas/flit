using System.Collections.Concurrent;
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

/// <summary>Manual listo para usar: artículos por slug y el <c>system</c> por perfil (HU #13023).</summary>
public sealed class DrFlitManualCatalog
{
    /// <summary>
    /// Una variante del <c>system</c> por perfil, construida una sola vez (HU #13023): el bloque del
    /// manual tiene que ser idéntico entre llamadas del mismo perfil para que la caché de Anthropic lo
    /// reconozca (AC2), y cada perfil paga solo los tokens de su audiencia (AC1).
    /// </summary>
    private readonly ConcurrentDictionary<DrFlitManualProfile, DrFlitSystemPrompt> _promptsByProfile = new();

    public DrFlitManualCatalog(IReadOnlyList<DrFlitManualArticle> articles)
    {
        ArgumentNullException.ThrowIfNull(articles);

        Articles = articles;
        BySlug = articles
            .GroupBy(a => a.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>Catálogo COMPLETO: la validación de citas sigue siendo contra todos los artículos (AC3).</summary>
    public IReadOnlyList<DrFlitManualArticle> Articles { get; }

    public IReadOnlyDictionary<string, DrFlitManualArticle> BySlug { get; }

    /// <summary>
    /// <c>system</c> de la variante del perfil: solo artículos de su audiencia más «Todos» (AC1). Un
    /// perfil sin artículos propios lleva al menos los de «Todos» y el chat sigue operativo (AC4). El
    /// orden es el del catálogo, estable entre llamadas.
    /// </summary>
    public DrFlitSystemPrompt GetSystemPrompt(DrFlitManualProfile profile) =>
        _promptsByProfile.GetOrAdd(profile, p => DrFlitPromptBuilder.Build(
            Articles.Where(a => DrFlitManualAudiences.IsVisible(a.Audience, p)).ToList()));
}
