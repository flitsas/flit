using System.Diagnostics;

namespace Flit.Api.Middleware;

/// <summary>
/// HU #13332 (Epic #13316): el identificador de correlación de una petición, de punta a punta. Toma el
/// <c>X-Correlation-Id</c> que puso el gateway (o el servicio que llama) y, si no viene, crea uno. Lo devuelve en la
/// respuesta, lo marca en la traza (<c>flit.correlation_id</c>, para buscarla por él) y lo abre como alcance de log
/// (<c>CorrelationId</c>), así cada línea de log de esta petición lo lleva.
/// </summary>
/// <remarks>
/// Un valor entrante que no parezca un id (vacío, larguísimo o con caracteres de control) se descarta y se crea uno
/// nuevo: la cabecera viene de afuera y termina en logs y en la respuesta.
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string TraceTag = "flit.correlation_id";
    public const string LogScopeKey = "CorrelationId";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Normalize(context.Request.Headers[HeaderName].FirstOrDefault()) ?? Guid.CreateVersion7().ToString();
        context.Request.Headers[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag(TraceTag, correlationId);

        using (_logger.BeginScope(new Dictionary<string, object> { [LogScopeKey] = correlationId }))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    /// <summary>El valor entrante si es un id razonable; <c>null</c> si hay que crear uno.</summary>
    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            return null;

        foreach (var c in trimmed)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
                return null;
        }

        return trimmed;
    }
}
