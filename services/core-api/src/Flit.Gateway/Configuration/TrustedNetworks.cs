using System.Net;
using System.Net.Sockets;

namespace Flit.Gateway.Configuration;

/// <summary>
/// Coincidencia de IP contra listas CIDR configuradas (HU #12417 AC1/AC5): usado para la excepción
/// del sello interno (<see cref="DomainSealOptions.InternalAllowedNetworks"/>). Sin listas
/// configuradas nada es de confianza (fail-closed).
/// </summary>
public static class TrustedNetworks
{
    public static bool Contains(IReadOnlyList<string> cidrs, IPAddress? address)
    {
        if (address is null || cidrs is null || cidrs.Count == 0)
        {
            return false;
        }

        foreach (var raw in cidrs)
        {
            if (TryParseCidr(raw, out var network, out var prefixLength) && IsInRange(address, network, prefixLength))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCidr(string value, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var addr))
        {
            return false;
        }

        var defaultPrefix = addr.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out prefixLength))
            {
                return false;
            }
        }
        else
        {
            prefixLength = defaultPrefix;
        }

        network = addr;
        return true;
    }

    private static bool IsInRange(IPAddress address, IPAddress network, int prefixLength)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != network.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != networkBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)~(0xFF >> remainingBits);
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}
