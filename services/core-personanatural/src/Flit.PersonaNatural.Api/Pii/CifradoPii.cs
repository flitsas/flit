using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flit.PersonaNatural.Api.Pii;

/// <summary>
/// Dato autenticado (AAD) de AES-GCM: de qué columna es el valor y, si aplica, de qué solicitud y de qué parte
/// (<c>parte:ordinal</c>). Un valor copiado a otra fila o a otra columna no se descifra (S-M10). Con solo la columna
/// (<see cref="DeColumna"/>) el AAD es esa cadena, como antes.
/// </summary>
public readonly record struct ContextoCifrado(string Columna, Guid? SolicitudId = null, string? Parte = null, int Ordinal = 0)
{
    public static ContextoCifrado DeColumna(string columna) => new(columna);

    public static implicit operator ContextoCifrado(string columna) => DeColumna(columna);

    /// <summary>Bytes del AAD: <c>columna|solicitud|parte:ordinal</c> (sin solicitud ni parte, solo la columna).</summary>
    public byte[] ComoAad()
    {
        ArgumentException.ThrowIfNullOrEmpty(Columna);
        var texto = SolicitudId is null && Parte is null
            ? Columna
            : string.Create(CultureInfo.InvariantCulture, $"{Columna}|{SolicitudId:N}|{Parte}:{Ordinal}");
        return Encoding.UTF8.GetBytes(texto);
    }
}

/// <summary>
/// Cifrado de datos personales en reposo con AES-256-GCM (HU #14037, Ley 1581). Cada valor sale como
/// <c>[versión de llave][nonce de 12][etiqueta de 16][cifrado]</c>: la versión viaja con el dato, así una llave nueva cifra
/// lo nuevo y las anteriores siguen abriendo lo viejo (rotación sin migrar datos). El contexto (<see cref="ContextoCifrado"/>:
/// columna, solicitud y <c>parte:ordinal</c>) entra como dato autenticado: un valor copiado a otra columna o a otra fila no se
/// puede descifrar. Formato v2 del AAD (HU #14037): lo cifrado con el AAD de solo columna ya no abre; aún no hay datos productivos.
/// Las llaves salen de la configuración (<c>Pii:Cifrado:Llaves:{versión}</c>, base64 de 32 bytes) y nunca del repositorio.
/// </summary>
public sealed class CifradoPii
{
    public const string SeccionLlaves = "Pii:Cifrado:Llaves";
    public const string ClaveVersionActiva = "Pii:Cifrado:VersionActiva";

    private const int Nonce = 12;
    private const int Etiqueta = 16;

    private readonly Dictionary<byte, byte[]> _llaves = [];

    public CifradoPii(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var hijo in configuration.GetSection(SeccionLlaves).GetChildren())
        {
            if (!byte.TryParse(hijo.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version == 0)
                throw new InvalidOperationException($"{SeccionLlaves.Replace(":", "__", StringComparison.Ordinal)}__{hijo.Key}: la versión debe ser un número entre 1 y 255.");
            _llaves[version] = Decodificar(hijo.Key, hijo.Value);
        }

        if (_llaves.Count == 0)
            throw new InvalidOperationException($"Falta {SeccionLlaves.Replace(":", "__", StringComparison.Ordinal)}__1 (en el .env: PERSONANATURAL_PII_AES_KEY; base64 de 32 bytes).");

        var texto = configuration[ClaveVersionActiva];
        VersionActiva = string.IsNullOrWhiteSpace(texto) ? _llaves.Keys.Max() : byte.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (byte)0;
        if (!_llaves.ContainsKey(VersionActiva))
            throw new InvalidOperationException($"{ClaveVersionActiva.Replace(":", "__", StringComparison.Ordinal)} apunta a una versión sin llave configurada.");
    }

    public byte VersionActiva { get; }

    public byte[]? Cifrar(string? texto, ContextoCifrado contexto)
    {
        if (texto is null)
            return null;

        var claro = Encoding.UTF8.GetBytes(texto);
        var salida = new byte[1 + Nonce + Etiqueta + claro.Length];
        salida[0] = VersionActiva;
        var nonce = salida.AsSpan(1, Nonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_llaves[VersionActiva], Etiqueta);
        aes.Encrypt(nonce, claro, salida.AsSpan(1 + Nonce + Etiqueta), salida.AsSpan(1 + Nonce, Etiqueta), contexto.ComoAad());
        return salida;
    }

    /// <summary>Abre un valor cifrado con la llave de su versión. Si lo tocaron o es de otra columna o fila, lanza <see cref="CryptographicException"/>.</summary>
    public string? Descifrar(byte[]? cifrado, ContextoCifrado contexto)
    {
        if (cifrado is null || cifrado.Length == 0)
            return null;
        if (cifrado.Length < 1 + Nonce + Etiqueta || !_llaves.TryGetValue(cifrado[0], out var llave))
            throw new CryptographicException("El dato protegido no se puede abrir: versión de llave desconocida o formato inválido.");

        var claro = new byte[cifrado.Length - 1 - Nonce - Etiqueta];
        using var aes = new AesGcm(llave, Etiqueta);
        aes.Decrypt(cifrado.AsSpan(1, Nonce), cifrado.AsSpan(1 + Nonce + Etiqueta), cifrado.AsSpan(1 + Nonce, Etiqueta), claro, contexto.ComoAad());
        return Encoding.UTF8.GetString(claro);
    }

    private static byte[] Decodificar(string version, string? base64)
    {
        try
        {
            var llave = Convert.FromBase64String(base64 ?? string.Empty);
            if (llave.Length == 32)
                return llave;
        }
        catch (FormatException)
        {
            // cae al mensaje de abajo
        }

        throw new InvalidOperationException($"{SeccionLlaves.Replace(":", "__", StringComparison.Ordinal)}__{version} debe ser base64 de exactamente 32 bytes.");
    }
}
