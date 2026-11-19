using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Platform.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;

/// <summary>
/// Sobre de la llave (ADR-0091 §2, composición JWE <c>alg=RSA-OAEP-256, enc=A256GCM</c>) y su AAD canónico (SP-11). El AAD lo
/// recompone Diagnóstico con sus propios valores (audiencia fija, herramienta, kid y nonce del pedido) y con proveedor, modelo,
/// <c>row_version</c> y <c>emitida_en</c> de la respuesta: un campo cambiado hace fallar el tag.
/// </summary>
public static partial class SobreLlaveIa
{
    /// <summary>Único valor v1 de <c>LlaveCifrada.alg</c>.</summary>
    public const string Alg = "RSA-OAEP-256+A256GCM";

    public const string VersionAad = "flit.ia.llave.v1";

    /// <summary>Audiencia fija en código: este sobre solo es para Diagnóstico.</summary>
    public const string Audiencia = "diagnostico";

    public const int BytesNonce = 16;

    public const int BytesIv = 12;

    public const int BytesTag = 16;

    public const int BytesCek = 32;

    /// <summary>Tope del texto cifrado (una llave de proveedor mide decenas de bytes).</summary>
    public const int MaxBytesTextoCifrado = 4096;

    private static readonly HashSet<string> Herramientas = new(StringComparer.Ordinal) { "ocr", "dr_flit", "diagnostico_dictamen" };

    private static readonly HashSet<string> Proveedores = new(StringComparer.Ordinal) { "anthropic", "openai", "kimi", "deepseek" };

    public static bool KidValido(string? kid) => kid is not null && PatronKid().IsMatch(kid);

    /// <summary>
    /// <c>flit.ia.llave.v1\ndiagnostico\n&lt;tool&gt;\n&lt;proveedor&gt;\n&lt;modelo&gt;\n&lt;row_version&gt;\n&lt;kid&gt;\n&lt;base64url(nonce)&gt;\n&lt;emitida_en s&gt;</c>
    /// en UTF-8. Valida antes de componer: ningún campo con salto de línea ni fuera de catálogo; números en decimal invariante.
    /// </summary>
    public static byte[] ComponerAad(string toolCode, string proveedor, string modelo, long rowVersion, string kid, ReadOnlySpan<byte> nonce, long emitidaEnSegundos)
    {
        if (toolCode is null || !Herramientas.Contains(toolCode))
            throw new ArgumentException("Herramienta fuera del catálogo.", nameof(toolCode));
        if (proveedor is null || !Proveedores.Contains(proveedor))
            throw new ArgumentException("Proveedor fuera del catálogo.", nameof(proveedor));
        if (modelo is null || !PatronModelo().IsMatch(modelo))
            throw new ArgumentException("Modelo con formato inválido.", nameof(modelo));
        if (!KidValido(kid))
            throw new ArgumentException("kid con formato inválido.", nameof(kid));
        if (nonce.Length != BytesNonce)
            throw new ArgumentException("El nonce debe medir 16 bytes.", nameof(nonce));

        var texto = string.Join('\n',
            VersionAad,
            Audiencia,
            toolCode,
            proveedor,
            modelo,
            rowVersion.ToString(CultureInfo.InvariantCulture),
            kid,
            Base64Url(nonce),
            emitidaEnSegundos.ToString(CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(texto);
    }

    private static string Base64Url(ReadOnlySpan<byte> datos) =>
        Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronKid();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronModelo();
}

/// <summary>Por qué un sobre no se intenta abrir (SP-9). Un tag que no cuadra no entra aquí: es <see cref="CryptographicException"/>.</summary>
public enum MotivoSobreInvalido
{
    AlgDesconocido,
    KidDistinto,
    Tamano,
}

/// <summary>Sobre con forma distinta de la v1. No lleva datos del sobre en el mensaje.</summary>
public sealed class SobreLlaveIaInvalidoException : Exception
{
    public SobreLlaveIaInvalidoException(MotivoSobreInvalido motivo)
        : base($"Sobre de llave IA inválido ({motivo}).") => Motivo = motivo;

    public SobreLlaveIaInvalidoException()
        : this(MotivoSobreInvalido.Tamano)
    {
    }

    public SobreLlaveIaInvalidoException(string message)
        : base(message)
    {
    }

    public SobreLlaveIaInvalidoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MotivoSobreInvalido Motivo { get; }
}

/// <summary>
/// Abre el sobre con la privada de Diagnóstico (SP-9): solo primitivas de .NET, OAEP con SHA-256 y AES-256-GCM con tag de 16 bytes.
/// La CEK y la llave en claro se ponen a cero al terminar (la <c>string</c> que devuelve es inevitable y vive solo en memoria).
/// </summary>
public static class DescifradorLlaveIa
{
    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <exception cref="SobreLlaveIaInvalidoException">alg, kid o tamaños distintos de la v1.</exception>
    /// <exception cref="CryptographicException">la CEK no se desenvuelve o el tag no cuadra (<see cref="AuthenticationTagMismatchException"/>).</exception>
    public static string Descifrar(LlaveCifrada sobre, string kidPedido, RSA privada, ReadOnlySpan<byte> aad)
    {
        ArgumentNullException.ThrowIfNull(sobre);
        ArgumentNullException.ThrowIfNull(privada);
        if (!string.Equals(sobre.Alg, SobreLlaveIa.Alg, StringComparison.Ordinal))
            throw new SobreLlaveIaInvalidoException(MotivoSobreInvalido.AlgDesconocido);
        if (!string.Equals(sobre.Kid, kidPedido, StringComparison.Ordinal))
            throw new SobreLlaveIaInvalidoException(MotivoSobreInvalido.KidDistinto);
        if (sobre.Iv.Length != SobreLlaveIa.BytesIv || sobre.Tag.Length != SobreLlaveIa.BytesTag
            || sobre.CekCifrada.Length != privada.KeySize / 8
            || sobre.TextoCifrado.Length is 0 or > SobreLlaveIa.MaxBytesTextoCifrado)
            throw new SobreLlaveIaInvalidoException(MotivoSobreInvalido.Tamano);

        var cek = privada.Decrypt(sobre.CekCifrada.ToByteArray(), RSAEncryptionPadding.OaepSHA256);
        var claro = new byte[sobre.TextoCifrado.Length];
        try
        {
            if (cek.Length != SobreLlaveIa.BytesCek)
                throw new CryptographicException("CEK de tamaño inválido.");
            using (var aes = new AesGcm(cek, SobreLlaveIa.BytesTag))
                aes.Decrypt(sobre.Iv.Span, sobre.TextoCifrado.Span, sobre.Tag.Span, claro, aad);
            try
            {
                return Utf8Estricto.GetString(claro);
            }
            catch (DecoderFallbackException)
            {
                throw new CryptographicException("Llave no es UTF-8.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
            CryptographicOperations.ZeroMemory(claro);
        }
    }
}

/// <summary>
/// Privadas de Diagnóstico por <c>kid</c> (actual y, durante una rotación, la anterior). Se importan una vez al arrancar desde el
/// archivo (SP-16) y los bytes leídos se ponen a cero. Una clave de menos de 3072 bits no se acepta (SP-9).
/// </summary>
public sealed class ClavesPrivadasIa : IDisposable
{
    public const int BitsMinimos = 3072;

    private readonly RSA _actual;
    private readonly RSA? _anterior;

    public ClavesPrivadasIa(string kid, RSA privada, string? kidAnterior = null, RSA? anterior = null)
    {
        ArgumentNullException.ThrowIfNull(privada);
        if (!SobreLlaveIa.KidValido(kid))
            throw new ArgumentException("kid con formato inválido.", nameof(kid));
        if (privada.KeySize < BitsMinimos)
            throw new ArgumentException("La clave privada debe tener al menos 3072 bits.", nameof(privada));
        if (anterior is not null && (!SobreLlaveIa.KidValido(kidAnterior) || anterior.KeySize < BitsMinimos || kidAnterior == kid))
            throw new ArgumentException("Clave anterior inválida.", nameof(anterior));
        Kid = kid;
        _actual = privada;
        KidAnterior = anterior is null ? null : kidAnterior;
        _anterior = anterior;
    }

    public string Kid { get; }

    public string? KidAnterior { get; }

    internal RSA? Para(string kid) =>
        string.Equals(kid, Kid, StringComparison.Ordinal) ? _actual
        : KidAnterior is not null && string.Equals(kid, KidAnterior, StringComparison.Ordinal) ? _anterior
        : null;

    /// <summary>
    /// Carga las privadas de <paramref name="opciones"/>. Null si la actual falta o no sirve; <paramref name="motivo"/> es un código fijo
    /// (sin rutas ni valores). Una anterior que no sirve se ignora y deja <paramref name="avisoAnterior"/>.
    /// </summary>
    public static ClavesPrivadasIa? Cargar(OpcionesIa opciones, out string motivo, out string? avisoAnterior)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        avisoAnterior = null;
        var kid = opciones.Kid?.Trim() ?? string.Empty;
        if (kid.Length == 0)
        {
            motivo = "SIN_KID";
            return null;
        }

        if (!SobreLlaveIa.KidValido(kid))
        {
            motivo = "KID_INVALIDO";
            return null;
        }

        if (string.IsNullOrWhiteSpace(opciones.ClavePrivadaArchivo))
        {
            motivo = "SIN_PRIVADA";
            return null;
        }

        var actual = Importar(opciones.ClavePrivadaArchivo.Trim(), out motivo);
        if (actual is null)
            return null;

        RSA? anterior = null;
        var kidAnterior = opciones.KidAnterior?.Trim();
        if (!string.IsNullOrEmpty(kidAnterior) || !string.IsNullOrWhiteSpace(opciones.ClavePrivadaAnteriorArchivo))
        {
            anterior = SobreLlaveIa.KidValido(kidAnterior) && kidAnterior != kid && !string.IsNullOrWhiteSpace(opciones.ClavePrivadaAnteriorArchivo)
                ? Importar(opciones.ClavePrivadaAnteriorArchivo.Trim(), out var motivoAnterior) is { } rsa ? rsa : Ignorar(out avisoAnterior, motivoAnterior)
                : Ignorar(out avisoAnterior, "ANTERIOR_INCOMPLETA");
        }

        motivo = "OK";
        return new ClavesPrivadasIa(kid, actual, anterior is null ? null : kidAnterior, anterior);
    }

    public void Dispose()
    {
        _actual.Dispose();
        _anterior?.Dispose();
    }

    private static RSA? Ignorar(out string? aviso, string motivo)
    {
        aviso = "ANTERIOR_" + motivo.Replace("ANTERIOR_", string.Empty, StringComparison.Ordinal);
        return null;
    }

    private static RSA? Importar(string ruta, out string motivo)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            motivo = "PRIVADA_ILEGIBLE";
            return null;
        }

        var texto = new char[bytes.Length];
        byte[]? der = null;
        var rsa = RSA.Create();
        try
        {
            var n = Encoding.ASCII.GetChars(bytes, texto);
            var contenido = texto.AsSpan(0, n).Trim();
            if (contenido.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                rsa.ImportFromPem(contenido);
            }
            else
            {
                der = new byte[contenido.Length];
                if (!Convert.TryFromBase64Chars(contenido, der, out var largo))
                    throw new FormatException("La privada no es base64.");
                rsa.ImportPkcs8PrivateKey(der.AsSpan(0, largo), out var leidos);
                if (leidos != largo)
                    throw new FormatException("La privada trae bytes de más.");
            }

            if (rsa.KeySize < BitsMinimos)
            {
                rsa.Dispose();
                motivo = "PRIVADA_MENOR_3072";
                return null;
            }

            motivo = "OK";
            return rsa;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            rsa.Dispose();
            motivo = "PRIVADA_ILEGIBLE";
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Array.Clear(texto);
            if (der is not null)
                CryptographicOperations.ZeroMemory(der);
        }
    }
}
