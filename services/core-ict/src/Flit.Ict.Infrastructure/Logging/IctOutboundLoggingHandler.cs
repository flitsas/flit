using System.Diagnostics;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Ict.Infrastructure.Logging;

/// <summary>
/// DelegatingHandler que registra en <c>ict.integration_log</c> cada llamada HTTP SALIENTE
/// (direction='outbound') con cabeceras REDACTADAS (barrera 1). Instrumenta los HttpClient de webhooks y
/// File Manager, que antes no dejaban rastro (solo el middleware inbound escribía). Best-effort: la
/// observabilidad NUNCA rompe la llamada. Escribe en un scope propio (sobrevive al ámbito del caller).
/// </summary>
public sealed class IctOutboundLoggingHandler(IServiceScopeFactory scopeFactory, string logType) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            return response;
        }
        finally
        {
            stopwatch.Stop();
            await WriteLogAsync(request, response, (int)stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task WriteLogAsync(HttpRequestMessage request, HttpResponseMessage? response, int durationMs)
    {
        try
        {
            var headers = request.Headers
                .Select(h => new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)));

            var log = new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = null, // egress: el handler no tiene contexto de tenant.
                LogType = logType,
                Direction = "outbound",
                Method = request.Method.Method,
                // Sin query string (evita filtrar secretos en la URL); conserva scheme+host+path del destino.
                Path = request.RequestUri?.GetLeftPart(UriPartial.Path) ?? string.Empty,
                StatusCode = response is null ? 0 : (int)response.StatusCode,
                Headers = IctSensitiveDataMasker.RedactHeaders(headers),
                CorrelationId = ResolveCorrelationId(request),
                DurationMs = durationMs,
                CreatedAt = DateTime.UtcNow,
            };

            using var scope = scopeFactory.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<IIntegrationLogWriter>();
            await writer.WriteAsync(log);
        }
#pragma warning disable CA1031 // el logging nunca debe romper la llamada saliente
        catch
        {
            // Swallow: la observabilidad no debe afectar la operación.
        }
#pragma warning restore CA1031
    }

    private static Guid? ResolveCorrelationId(HttpRequestMessage request) =>
        request.Headers.TryGetValues("X-Correlation-Id", out var values)
            && Guid.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : Guid.NewGuid();
}
