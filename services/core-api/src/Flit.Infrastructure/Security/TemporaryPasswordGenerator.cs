using System.Security.Cryptography;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Security;

/// <summary>
/// Genera una contraseña temporal de 14 caracteres que garantiza al menos una mayúscula,
/// una minúscula, un dígito y un símbolo (cumple la política de complejidad de la HU #10171).
/// Se excluyen caracteres ambiguos (O/0, l/1, I) para facilitar su transcripción.
/// </summary>
public sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*?";
    private const string All = Upper + Lower + Digits + Symbols;
    private const int Length = 14;

    public string Generate()
    {
        var chars = new List<char> { Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols) };

        while (chars.Count < Length)
            chars.Add(Pick(All));

        // Mezcla Fisher–Yates con RNG criptográfico para no exponer las posiciones fijas.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string([.. chars]);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
