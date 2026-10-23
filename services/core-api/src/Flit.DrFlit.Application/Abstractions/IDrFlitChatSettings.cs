namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Ajustes del chat que decide la aplicación (no el adaptador del modelo). Los lee Infrastructure de
/// <c>Anthropic:DrFlit*</c>; aquí no hay valores fijos.
/// </summary>
public interface IDrFlitChatSettings
{
    /// <summary><c>Anthropic:DrFlitDailyMessageLimit</c>: mensajes al LLM por usuario, tenant y día.</summary>
    int DailyMessageLimit { get; }

    /// <summary><c>Anthropic:DrFlitEnabled</c>: con <c>false</c> el chat responde siempre degradado.</summary>
    bool Enabled { get; }
}
