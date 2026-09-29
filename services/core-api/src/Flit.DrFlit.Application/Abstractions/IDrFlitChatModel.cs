using Flit.DrFlit.Application.Chat;

namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Puerto hacia el LLM del chat de DR. FLIT (ADR-0060). La implementación de Infrastructure lee modelo,
/// tope de salida y timeout de <c>Anthropic:DrFlit*</c>; aquí no hay ningún valor fijo. Sin tool-use ni
/// function-calling: la única salida posible es texto.
/// </summary>
public interface IDrFlitChatModel
{
    Task<DrFlitModelCallResult> CompleteAsync(
        DrFlitSystemPrompt system,
        IReadOnlyList<DrFlitTurn> turns,
        CancellationToken ct);
}

/// <summary>Desenlace de una llamada al modelo.</summary>
public enum DrFlitModelCallStatus
{
    /// <summary>El proveedor respondió con texto. Todavía falta validarlo contra el contrato.</summary>
    Ok,

    /// <summary>
    /// El LLM está apagado por configuración (<c>Anthropic:DrFlitEnabled=false</c>) o no tiene API key.
    /// No se hizo ninguna llamada HTTP.
    /// </summary>
    Disabled,

    /// <summary>Timeout, error de transporte o respuesta no utilizable tras el reintento.</summary>
    Failed,
}

/// <summary>
/// Resultado de <see cref="IDrFlitChatModel.CompleteAsync"/>. Los contadores de tokens alimentan el log de
/// costo (§4 del diseño); nunca se loguea <see cref="Text"/>.
/// </summary>
public sealed record DrFlitModelCallResult(
    DrFlitModelCallStatus Status,
    string? Text = null,
    DrFlitTokenUsage? Usage = null)
{
    public static DrFlitModelCallResult Disabled() => new(DrFlitModelCallStatus.Disabled);

    public static DrFlitModelCallResult Failed() => new(DrFlitModelCallStatus.Failed);
}

/// <summary>Tokens reportados por el proveedor para una respuesta.</summary>
public sealed record DrFlitTokenUsage(
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens,
    int CacheCreationInputTokens);
