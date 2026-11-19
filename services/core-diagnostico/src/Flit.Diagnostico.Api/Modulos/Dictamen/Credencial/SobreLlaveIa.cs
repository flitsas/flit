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

    /// <summary>Solo <c>[a-z0-9-]{1,64}</c>; <c>\z</c> y no <c>$</c>, que aceptaría un salto de línea final (CR-1).</summary>
    public static bool KidValido(string? kid) => kid is not null && PatronKid().IsMatch(kid);

    /// <summary>Formato de modelo que entra al AAD y al log; <c>\z</c> y no <c>$</c> (CR-1).</summary>
    public static bool ModeloValido(string? modelo) => modelo is not null && PatronModelo().IsMatch(modelo);

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
        if (!ModeloValido(modelo))
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

    [GeneratedRegex(@"^[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PatronKid();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\z", RegexOptions.CultureInvariant)]
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
/// Privadas de Diagnóstico por <c>kid</c> (actual y, durante una rotación, la anterior). Se importan una vez al arrancar desde la
/// variable de entorno o, si está vacía, desde el archivo (SP-16), y los búferes leídos se ponen a cero. Una clave de menos de 3072 bits no se acepta (SP-9). Del PEM solo se acepta la
/// etiqueta <c>PRIVATE KEY</c> (PKCS#8) y toda clave cargada pasa una ida y vuelta OAEP-SHA256: una pública (<c>PUBLIC KEY</c>,
/// <c>RSA PUBLIC KEY</c>) da <c>PRIVADA_NO_ES_PRIVADA</c> y el redactor queda sin configurar (CR-4).
/// </summary>
public sealed class ClavesPrivadasIa : IDisposable
{
    public const int BitsMinimos = 3072;

    public const string EtiquetaPem = "PRIVATE KEY";

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

        var actual = Importar(opciones.ClavePrivada, opciones.ClavePrivadaArchivo, out motivo);
        if (actual is null)
            return null;

        RSA? anterior = null;
        var kidAnterior = opciones.KidAnterior?.Trim();
        var hayAnterior = !string.IsNullOrWhiteSpace(opciones.ClavePrivadaAnterior) || !string.IsNullOrWhiteSpace(opciones.ClavePrivadaAnteriorArchivo);
        if (!string.IsNullOrEmpty(kidAnterior) || hayAnterior)
        {
            anterior = SobreLlaveIa.KidValido(kidAnterior) && kidAnterior != kid && hayAnterior
                ? Importar(opciones.ClavePrivadaAnterior, opciones.ClavePrivadaAnteriorArchivo, out var motivoAnterior) is { } rsa ? rsa : Ignorar(out avisoAnterior, motivoAnterior)
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

    /// <summary>
    /// La variable (<paramref name="enLinea"/>) gana si tiene valor; si no, el archivo. Ambas pasan por <see cref="ImportarContenido"/>.
    /// <c>SIN_PRIVADA</c> si no hay ninguna.
    /// </summary>
    private static RSA? Importar(string? enLinea, string? archivo, out string motivo)
    {
        if (!string.IsNullOrWhiteSpace(enLinea))
            return ImportarEnLinea(enLinea, out motivo);
        if (!string.IsNullOrWhiteSpace(archivo))
            return ImportarArchivo(archivo.Trim(), out motivo);
        motivo = "SIN_PRIVADA";
        return null;
    }

    /// <summary>
    /// Copia la variable a un búfer propio y convierte la secuencia de dos caracteres <c>\n</c> en salto de línea (PEM en una línea
    /// de un <c>.env</c>). El búfer se limpia al terminar; la <c>string</c> de configuración es inmutable y no se puede borrar.
    /// </summary>
    private static RSA? ImportarEnLinea(string enLinea, out string motivo)
    {
        var texto = new char[enLinea.Length];
        try
        {
            var n = 0;
            for (var i = 0; i < enLinea.Length; i++)
            {
                if (enLinea[i] == '\\' && i + 1 < enLinea.Length && enLinea[i + 1] == 'n')
                {
                    texto[n++] = '\n';
                    i++;
                }
                else
                {
                    texto[n++] = enLinea[i];
                }
            }

            return ImportarContenido(texto.AsSpan(0, n), out motivo);
        }
        finally
        {
            Array.Clear(texto);
        }
    }

    private static RSA? ImportarArchivo(string ruta, out string motivo)
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
        try
        {
            var n = Encoding.ASCII.GetChars(bytes, texto);
            return ImportarContenido(texto.AsSpan(0, n), out motivo);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Array.Clear(texto);
        }
    }

    /// <summary>Base64 DER PKCS#8 o PEM <c>PRIVATE KEY</c>; al menos 3072 bits e ida y vuelta OAEP (CR-4). Los bytes DER se ponen a cero.</summary>
    private static RSA? ImportarContenido(ReadOnlySpan<char> texto, out string motivo)
    {
        byte[]? der = null;
        var rsa = RSA.Create();
        try
        {
            var contenido = texto.Trim();
            ReadOnlySpan<char> base64 = contenido;
            if (contenido.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                if (!PemEncoding.TryFind(contenido, out var campos))
                    throw new FormatException("PEM ilegible.");
                if (!contenido[campos.Label].SequenceEqual(EtiquetaPem.AsSpan()))
                {
                    rsa.Dispose();
                    motivo = "PRIVADA_NO_ES_PRIVADA";
                    return null;
                }

                base64 = contenido[campos.Base64Data];
            }

            der = new byte[base64.Length];
            if (!Convert.TryFromBase64Chars(base64, der, out var largo))
                throw new FormatException("La privada no es base64.");

            rsa.ImportPkcs8PrivateKey(der.AsSpan(0, largo), out var leidos);
            if (leidos != largo)
                throw new FormatException("La privada trae bytes de más.");

            if (rsa.KeySize < BitsMinimos)
            {
                rsa.Dispose();
                motivo = "PRIVADA_MENOR_3072";
                return null;
            }

            if (!IdaYVueltaOaep(rsa))
            {
                rsa.Dispose();
                motivo = "PRIVADA_NO_ES_PRIVADA";
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
            if (der is not null)
                CryptographicOperations.ZeroMemory(der);
        }
    }

    /// <summary>Cifra 32 bytes aleatorios con OAEP-SHA256 y los descifra: solo una privada completa los devuelve iguales.</summary>
    private static bool IdaYVueltaOaep(RSA rsa)
    {
        var prueba = RandomNumberGenerator.GetBytes(SobreLlaveIa.BytesCek);
        byte[]? vuelta = null;
        try
        {
            var ida = rsa.Encrypt(prueba, RSAEncryptionPadding.OaepSHA256);
            vuelta = rsa.Decrypt(ida, RSAEncryptionPadding.OaepSHA256);
            return CryptographicOperations.FixedTimeEquals(prueba, vuelta);
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prueba);
            if (vuelta is not null)
                CryptographicOperations.ZeroMemory(vuelta);
        }
    }
}
