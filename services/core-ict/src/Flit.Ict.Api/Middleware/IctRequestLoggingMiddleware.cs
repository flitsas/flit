using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.Logging;

namespace Flit.Ict.Api.Middleware;

/// <summary>
/// Registra cada request entrante de CLIENTE ICT (rutas v1) en ict.integration_log con cabeceras y CUERPO
/// (request + response) REDACTADOS (barrera 1: nunca persiste tokens ni PII sin enmascarar). El cuerpo se
/// captura con tope de tamaño y se enmascara de forma recursiva (IctSensitiveDataMasker.MaskJsonBody).
/// Escribe en un scope propio para que el rastro sobreviva a un rollback del caso de uso.
/// </summary>
public sealed class IctRequestLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
{
    /// <summary>Tope de captura del cuerpo (request/response). Por encima se registra un placeholder, no el cuerpo.</summary>
    private const int MaxBodyCaptureBytes = 64 * 1024;

    /// <summary>
    /// Prefijos de rutas de CLIENTE ICT que se registran en el log: las rutas v1 que usan los clientes
    /// (compat) más las mejoras v2 de pre-trámite. NO se incluye <c>/api/v1/ict</c> (observabilidad del
    /// submódulo frontend) para no auto-loguear las consultas del propio panel de logs.
    /// </summary>
    private static readonly string[] LoggedPrefixes =
    [
        "/api/v1/auth",                 // login ICT (v1)
        "/api/v1/external-transaction", // register, abortProcess, pauseDraftProcess (v1)
        "/api/v1/status-process",       // estado (v1)
        "/api/v1/transactions",         // reproceso (v1)
        "/api/v1/secretaries",          // secretarías (v1)
        "/api/v1/transact-attachments", // adjuntos (v1)
        "/api/v1/pretramites",          // edición + adjuntos de pre-trámite (mejoras v2)
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!ShouldLog(path))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        // Request: se captura ANTES de que lo lea el endpoint (EnableBuffering + rebobinado). Solo JSON.
        var requestBody = await CaptureRequestBodyAsync(context);

        // Response: se sustituye el stream por uno que ESCRIBE DE PASO al original y captura hasta el tope.
        var originalResponseBody = context.Response.Body;
        await using var capture = new CaptureStream(originalResponseBody, MaxBodyCaptureBytes);
        context.Response.Body = capture;

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            context.Response.Body = originalResponseBody;
            var responseBody = ExtractResponseBody(capture, context.Response.ContentType);
            await WriteLogAsync(context, path, (int)stopwatch.ElapsedMilliseconds, requestBody, responseBody);
        }
    }

    private static bool ShouldLog(string path) =>
        Array.Exists(LoggedPrefixes, p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsJson(string? contentType) =>
        contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> CaptureRequestBodyAsync(HttpContext context)
    {
        var request = context.Request;
        // Solo cuerpos JSON: un multipart de adjunto (bytes de archivo) o binario NO se captura.
        if (!IsJson(request.ContentType))
        {
            return null;
        }

        request.EnableBuffering();
        var (bytes, exceeded) = await ReadCappedAsync(request.Body, MaxBodyCaptureBytes, context.RequestAborted);
        request.Body.Position = 0;
        return exceeded
            ? "<cuerpo grande omitido del log>"
            : IctSensitiveDataMasker.MaskJsonBody(Encoding.UTF8.GetString(bytes));
    }

    private static string? ExtractResponseBody(CaptureStream capture, string? contentType)
    {
        if (!IsJson(contentType))
        {
            return null;
        }

        return capture.Exceeded
            ? "<cuerpo grande omitido del log>"
            : IctSensitiveDataMasker.MaskJsonBody(capture.GetText(Encoding.UTF8));
    }

    private static async Task<(byte[] Bytes, bool Exceeded)> ReadCappedAsync(Stream stream, int cap, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > cap)
            {
                var remaining = (int)(cap - buffer.Length);
                if (remaining > 0)
                {
                    buffer.Write(chunk, 0, remaining);
                }

                return (buffer.ToArray(), true);
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }

    private async Task WriteLogAsync(
        HttpContext context, string path, int durationMs, string? requestBody, string? responseBody)
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
                Request = requestBody,
                Response = responseBody,
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

    /// <summary>
    /// Stream de captura del cuerpo de respuesta: ESCRIBE DE PASO al stream original (el cliente recibe
    /// todo) y guarda a lo sumo <c>captureLimit</c> bytes para el log. Así un download grande no se
    /// bufferiza entero: solo se retiene el tope, y <see cref="Exceeded"/> avisa que se recortó.
    /// </summary>
    private sealed class CaptureStream(Stream inner, int captureLimit) : Stream
    {
        private readonly MemoryStream _captured = new();

        public bool Exceeded { get; private set; }

        public string GetText(Encoding encoding) => encoding.GetString(_captured.ToArray());

        private void Capture(ReadOnlySpan<byte> data)
        {
            var room = captureLimit - (int)_captured.Length;
            if (room <= 0)
            {
                Exceeded = true;
                return;
            }

            if (data.Length > room)
            {
                _captured.Write(data[..room]);
                Exceeded = true;
            }
            else
            {
                _captured.Write(data);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captured.Dispose();
            }

            // NO se desecha `inner`: es el stream de respuesta original, propiedad del framework.
            base.Dispose(disposing);
        }
    }
}
