namespace Flit.Admin.Application.Companies.Domains.Verification;

/// <summary>
/// Comprobación del TXT de titularidad reducida a los cuatro casos de <see cref="DnsCheckOutcome"/>. La comparten la comprobación
/// a demanda de marca blanca y la del dominio del portal de un organismo. Nunca loguea el valor crudo del TXT.
/// </summary>
internal static class DomainTxtCheck
{
    private const string TokenPrefix = "flit-verify=";

    public static async Task<DnsCheckOutcome> EvaluarAsync(IDnsTxtResolver dnsResolver, string host, string expectedToken, CancellationToken cancellationToken)
    {
        var lookup = await dnsResolver.LookupAsync(host, cancellationToken).ConfigureAwait(false);
        if (lookup.Error is not null)
        {
            return DnsCheckOutcome.Error;
        }

        if (!lookup.Found || lookup.Values.Count == 0)
        {
            return DnsCheckOutcome.NotFound;
        }

        return lookup.Values.Any(v => string.Equals(Normalizar(v), expectedToken, StringComparison.Ordinal))
            ? DnsCheckOutcome.Matches
            : DnsCheckOutcome.Mismatch;
    }

    /// <summary>El registro TXT suele publicarse como <c>flit-verify=&lt;token&gt;</c>; también se acepta el token desnudo.</summary>
    private static string Normalizar(string rawValue)
    {
        var trimmed = rawValue.Trim().Trim('"');
        return trimmed.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[TokenPrefix.Length..]
            : trimmed;
    }
}
