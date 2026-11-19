using System.Security.Cryptography;
using System.Text;
using Flit.Admin.Application.Plataforma.Ai.Entrega;

namespace Flit.Infrastructure.Ai.Entrega;

/// <summary>
/// Sobre híbrido de la llave de un proveedor (ADR-0091 §2, SP-9/SP-10): CEK de 32 bytes e IV de 12 nuevos por sobre
/// (<see cref="RandomNumberGenerator"/>), <c>AES-256-GCM</c> con tag de 16 bytes sobre el UTF-8 de la llave y el AAD canónico,
/// y la CEK envuelta con <c>RSA-OAEP</c> SHA-256 para la pública del destinatario (≥ 3072 bits). Solo primitivas in-box; sin
/// estado ni caché: la CEK y el texto en claro se ponen a cero al salir.
/// </summary>
internal static class SelladorLlaveIa
{
    public const string Alg = "RSA-OAEP-256+A256GCM";
    public const int MinBitsRsa = 3072;
    public const int BytesCek = 32;
    public const int BytesIv = 12;
    public const int BytesTag = 16;

    public static SobreLlaveIa Sellar(RSA publica, string kid, string llave, byte[] aad)
    {
        var cek = new byte[BytesCek];
        var iv = new byte[BytesIv];
        try
        {
            RandomNumberGenerator.Fill(cek);
            RandomNumberGenerator.Fill(iv);
            return SellarCon(publica, kid, llave, aad, cek, iv);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
        }
    }

    /// <summary>Con CEK e IV dados: solo para <see cref="Sellar"/> y para el vector de prueba compartido. No reutilizar CEK/IV.</summary>
    internal static SobreLlaveIa SellarCon(RSA publica, string kid, string llave, byte[] aad, byte[] cek, byte[] iv)
    {
        ArgumentNullException.ThrowIfNull(publica);
        ArgumentException.ThrowIfNullOrEmpty(llave);
        ArgumentNullException.ThrowIfNull(aad);
        ArgumentNullException.ThrowIfNull(cek);
        ArgumentNullException.ThrowIfNull(iv);
        if (publica.KeySize < MinBitsRsa)
            throw new CryptographicException("La clave pública del destinatario es menor de 3072 bits.");
        if (cek.Length != BytesCek || iv.Length != BytesIv)
            throw new CryptographicException("Tamaño de CEK o IV inválido.");

        var plano = Encoding.UTF8.GetBytes(llave);
        try
        {
            var cifrado = new byte[plano.Length];
            var tag = new byte[BytesTag];
            using (var aes = new AesGcm(cek, BytesTag))
                aes.Encrypt(iv, plano, cifrado, tag, aad);

            var cekCifrada = publica.Encrypt(cek, RSAEncryptionPadding.OaepSHA256);
            return new SobreLlaveIa(Alg, kid, cekCifrada, (byte[])iv.Clone(), cifrado, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plano);
        }
    }
}
