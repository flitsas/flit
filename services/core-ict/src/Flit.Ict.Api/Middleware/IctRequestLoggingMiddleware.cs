using System.Diagnostics;
using System.Security.Claims;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.Logging;

namespace Flit.Ict.Api.Middleware;

/// <summary>
/// Registra cada request entrante a /api/ict/** en ict.integration_log con cabeceras REDACTADAS
/// (barrera 1: nunca persiste tokens ni cuerpos crudos). Escribe en un scope propio para que el
/// rastro sobreviva a un rollback del caso de uso.
/// </summary>
public sealed class IctRequestLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/api/ict", StringComparison.OrdinalIgnoreCase))
            {
                await WriteLogAsync(context, path, (int)stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private async Task WriteLogAsync(HttpContext context, string path, int durationMs)
    {
        try
        {
            var headers = context.Request.Headers
                .Select(h => new KeyValuePair<string, string>(h.Key, h.Value.ToString()));

            var log = new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = ParseGuid(context.User.FindFirstValue("tenant_id")),
                LogType = path.Contains("/auth", StringComparison.OrdinalIgnoreCase) ? "auth" : "transaction",
                Direction = "inbound",
                Method = context.Request.Method,
                Path = path,
                StatusCode = context.Response.StatusCode,
                Headers = IctSensitiveDataMasker.RedactHeaders(headers),
                CorrelationId = ResolveCorrelationId(context),
                DurationMs = durationMs,
                Usuario = context.User.FindFirstValue("sub"),
                CreatedAt = DateTime.UtcNow,
            };

            using var scope = scopeFactory.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<IIntegrationLogWriter>();
            await writer.WriteAsync(log, context.RequestAborted);
        }
#pragma warning disable CA1031 // el logging nunca debe romper la request
        catch
        {
            // Swallow: la observabilidad no debe afectar la operación.
        }
#pragma warning restore CA1031
    }

    private static Guid? ResolveCorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Correlation-Id", out var value)
            && Guid.TryParse(value.ToString(), out var parsed)
            ? parsed
            : Guid.NewGuid();

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}
