using System.Net;
using System.Net.Sockets;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Guard anti-SSRF del <c>target_url</c> de los webhooks. El destino proviene del payload de ingesta
/// (<c>url_web_hook</c>), así que antes de hacer el POST se valida que sea http(s) público y NO apunte a
/// direcciones internas (loopback, rangos privados RFC1918, link-local, unique-local IPv6 ni la IP de
/// metadata de la nube 169.254.169.254). Fail-closed: cualquier error de parseo/resolución ⇒ bloqueado.
/// TODO(ICT-WEBHOOK-ALLOWLIST): endurecer con allowlist de hosts por tenant (plan §A.9).
/// </summary>
public static class WebhookTargetGuard
{
    private static readonly IPAddress MetadataV4 = IPAddress.Parse("169.254.169.254");

    public static async Task<bool> IsPublicHttpTargetAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            }
            catch (SocketException)
            {
                return false; // no resuelve → fail-closed
            }
        }

        if (addresses.Length == 0)
        {
            return false;
        }

        // Si CUALQUIER dirección resuelta es interna, se bloquea (evita DNS rebinding a un rango privado).
        foreach (var ip in addresses)
        {
            if (!IsPublic(ip))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPublic(IPAddress address)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(ip) || ip.Equals(MetadataV4))
        {
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] is 0 or 10 or 127)
            {
                return false; // 0.0.0.0/8, 10/8, 127/8
            }

            if (b[0] == 169 && b[1] == 254)
            {
                return false; // 169.254/16 link-local
            }

            if (b[0] == 172 && b[1] is >= 16 and <= 31)
            {
                return false; // 172.16/12
            }

            if (b[0] == 192 && b[1] == 168)
            {
                return false; // 192.168/16
            }

            if (b[0] == 100 && b[1] is >= 64 and <= 127)
            {
                return false; // 100.64/10 CGNAT
            }

            if (b[0] == 198 && b[1] is 18 or 19)
            {
                return false; // 198.18/15 benchmarking
            }

            if (b[0] == 192 && b[1] == 0 && b[2] == 0)
            {
                return false; // 192.0.0/24
            }

            return b[0] < 240; // 240/4 reservado + broadcast
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return false;
            }

            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) != 0xFC; // fc00::/7 unique-local
        }

        return false;
    }
}
