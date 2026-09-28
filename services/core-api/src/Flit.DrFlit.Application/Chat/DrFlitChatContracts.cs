using Flit.DrFlit.Application.Manual;

namespace Flit.DrFlit.Application.Chat;

/// <summary>Estado de negocio de una respuesta del chat (ADR-0060 §5.1). Siempre viaja con HTTP 200.</summary>
public enum DrFlitChatStatus
{
    /// <summary>El LLM respondió y la respuesta pasó la validación.</summary>
    Ok,

    /// <summary>
    /// LLM apagado, caído, fuera de contrato o sin manual cargado. El frontend responde con su buscador
    /// local por palabras clave.
    /// </summary>
    Degraded,

    /// <summary>El usuario ya alcanzó el tope diario en este tenant. No se llamó al LLM.</summary>
    RateLimited,
}

/// <summary>Valores de <see cref="DrFlitChatStatus"/> en el contrato JSON.</summary>
public static class DrFlitChatStatusWire
{
    public static string ToWire(this DrFlitChatStatus status) => status switch
    {
        DrFlitChatStatus.Ok => "ok",
        DrFlitChatStatus.Degraded => "degraded",
        DrFlitChatStatus.RateLimited => "rate_limited",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

/// <summary>Mensaje libre del usuario, con el contexto que ya validó el endpoint.</summary>
/// <param name="History">Turnos previos de la conversación (los guarda el cliente), del más viejo al más nuevo.</param>
/// <param name="Profile">Perfil efectivo del rol del JWT (HU #13023): acota el manual del <c>system</c> a su audiencia.</param>
public sealed record DrFlitChatRequest(
    Guid TenantId,
    Guid UserId,
    string Message,
    IReadOnlyList<DrFlitTurn> History,
    DrFlitManualProfile Profile = DrFlitManualProfile.Gestor);

/// <summary>Respuesta del chat lista para serializar.</summary>
/// <param name="Intent">Intención clasificada; <c>null</c> cuando no hubo respuesta válida del LLM.</param>
/// <param name="Citations">Artículos citados y verificados contra el catálogo.</param>
/// <param name="GestionTarget">HU #12927 — búsqueda sugerida para la intención gestión, o <c>null</c>.</param>
public sealed record DrFlitChatResult(
    DrFlitChatStatus Status,
    DrFlitIntent? Intent,
    string Reply,
    IReadOnlyList<DrFlitManualArticle> Citations,
    int MessagesUsedToday,
    int DailyLimit,
    string? GestionTarget = null);

/// <summary>
/// Ensamblador del chat de DR. FLIT (HU #12919): tope diario + manual + LLM + validación + degradación.
/// </summary>
public interface IDrFlitAssistant
{
    Task<DrFlitChatResult> AskAsync(DrFlitChatRequest request, CancellationToken ct);
}
