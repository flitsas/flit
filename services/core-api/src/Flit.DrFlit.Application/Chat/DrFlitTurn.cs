namespace Flit.DrFlit.Application.Chat;

/// <summary>Autor de un turno de la conversación enviada al modelo.</summary>
public enum DrFlitTurnRole
{
    User,
    Assistant,
}

/// <summary>
/// Un turno de la conversación. Viaja en <c>messages</c> de Anthropic, nunca en <c>system</c>: el texto
/// del usuario es dato, no instrucción (guardarraíl §8.2.1 de ADR-0060).
/// </summary>
public sealed record DrFlitTurn(DrFlitTurnRole Role, string Text);

/// <summary>
/// Bloques del <c>system</c> de la llamada, en el orden en que se envían: primero el manual y después
/// las instrucciones. Ambos son estables entre mensajes, así que los dos se marcan cacheables.
/// </summary>
public sealed record DrFlitSystemPrompt(string ManualBlock, string Instructions);
