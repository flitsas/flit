using DnsClient;
using Flit.Admin.Application.Companies.Domains.Verification;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Domains;

/// <summary>
/// Resolutor DNS real de la comprobación de titularidad (HU #12425 AC1/AC2/AC7). Paquete
/// <c>DnsClient</c> 1.8.0 (auditoría <c>.claude/state/marca-blanca/auditoria-dnsclient.md</c>, APROBADO
/// CON CONDICIONES): <see cref="LookupClientOptions.Timeout"/> corto y configurable, servidores DNS
/// explícitos por <see cref="DomainVerificationOptions.DnsServers"/> (vacío ⇒ resolutor del sistema,
/// condición 2), caché habilitada (TTL del propio registro) y fallback a TCP para TXT largos
/// (condición 2). Nunca sigue CNAME de forma indefinida: <c>LookupClient</c> resuelve un único
/// registro TXT del nombre pedido, sin recursión manual. Nunca loguea el valor crudo del TXT
/// (condición 5) — solo el host y si se encontró.
/// </summary>
internal sealed class DnsClientTxtResolver : IDnsTxtResolver
{
    private const string VerificationLabel = "_flit-verify";

    private readonly LookupClient _lookupClient;
    private readonly ILogger<DnsClientTxtResolver> _logger;

    public DnsClientTxtResolver(DomainVerificationOptions options, ILogger<DnsClientTxtResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var lookupOptions = options.DnsServers.Count > 0
            ? new LookupClientOptions(options.DnsServers.Select(ParseNameServer).ToArray())
            : new LookupClientOptions();

        lookupOptions.Timeout = options.DnsTimeout;
        lookupOptions.UseCache = true;
        lookupOptions.UseTcpFallback = true;
        lookupOptions.ThrowDnsErrors = false;
        lookupOptions.Retries = 1;

        _lookupClient = new LookupClient(lookupOptions);
    }

    public async Task<DnsTxtLookupResult> LookupAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var query = $"{VerificationLabel}.{host}";
        try
        {
            var response = await _lookupClient.QueryAsync(query, QueryType.TXT, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.HasError)
            {
                DnsResolverLog.QueryError(_logger, host, response.ErrorMessage);
                return DnsTxtLookupResult.Failure(response.ErrorMessage ?? "DNS_ERROR");
            }

            var values = response.Answers.TxtRecords()
                .SelectMany(r => r.Text)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            DnsResolverLog.QueryCompleted(_logger, host, values.Count > 0);
            return values.Count > 0 ? DnsTxtLookupResult.Success(values) : DnsTxtLookupResult.NotFound();
        }
        catch (DnsResponseException ex)
        {
            DnsResolverLog.QueryException(_logger, host, ex);
            return DnsTxtLookupResult.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DnsResolverLog.QueryException(_logger, host, ex);
            return DnsTxtLookupResult.Failure(ex.Message);
        }
    }

    private static NameServer ParseNameServer(string raw) =>
        System.Net.IPAddress.TryParse(raw, out var ip) ? new NameServer(ip) : new NameServer(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(raw), 53));
}

/// <summary>Logging source-generado (CA1848). NUNCA incluye el valor crudo del TXT (auditoría DnsClient condición 5) — solo host + resultado booleano.</summary>
internal static partial class DnsResolverLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Comprobación TXT de {Host}: encontrado={Found}.")]
    public static partial void QueryCompleted(ILogger logger, string host, bool found);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comprobación TXT de {Host} respondió con error: {Error}.")]
    public static partial void QueryError(ILogger logger, string host, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comprobación TXT de {Host} falló.")]
    public static partial void QueryException(ILogger logger, string host, Exception ex);
}
