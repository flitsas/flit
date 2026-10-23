namespace Flit.DrFlit.Application.Manual;

/// <summary>
/// Artículo del manual tal como lo consume DR. FLIT (Épica #12718, ADR-0060). El manual se escribe en
/// <c>frontend/lib/manual/</c> (única fuente de verdad) y llega al backend como artefacto generado
/// (HU #12920); el backend nunca lo edita, solo lo usa como contexto del LLM y para validar citas.
/// </summary>
/// <param name="Slug">Identificador estable del artículo. Es lo único que el modelo puede citar.</param>
/// <param name="Title">Título visible, para armar la cita.</param>
/// <param name="Href">Ruta del artículo en el portal <c>/manual</c>.</param>
/// <param name="Text">Contenido del artículo en texto plano (resumen + bloques), ya aplanado.</param>
/// <param name="SourceHref">Primera fuente de respaldo (norma, anexo), si la tiene.</param>
/// <param name="PrimarySource">True si el artículo es la norma que avala la plataforma.</param>
/// <param name="Audience">Audiencia del artículo tal como viene en el artefacto (HU #13023); <c>null</c> = visible para todos.</param>
public sealed record DrFlitManualArticle(
    string Slug,
    string Title,
    string Href,
    string Text,
    string? SourceHref = null,
    bool PrimarySource = false,
    string? Audience = null);
