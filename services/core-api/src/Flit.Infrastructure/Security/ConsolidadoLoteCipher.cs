using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Microsoft.AspNetCore.DataProtection;

namespace Flit.Infrastructure.Security;

/// <summary>
/// AES-256-GCM por bloques en streaming para las partes del lote de consolidados (épica #13216, HU #13372,
/// ADR-0070 §D4 opción C y adenda v6). Solo usa la BCL (<see cref="AesGcm"/>, <see cref="HKDF"/>,
/// <see cref="RandomNumberGenerator"/>) y Data Protection.
/// <para><b>Clave del lote (DEK).</b> 32 bytes de <see cref="RandomNumberGenerator"/>, envueltos con
/// <c>IDataProtectionProvider.CreateProtector("Flit.Tramites.ConsolidadoLote.Dek.v1")</c> (propósito versionado; el
/// keyring vive en BD, HU #10233). La DEK en claro solo existe dentro de esta clase: se desenvuelve al empezar cada
/// operación, solo se usa como IKM de la HKDF y se pone a cero con <see cref="CryptographicOperations.ZeroMemory"/> en un
/// <c>finally</c>. Los búferes de texto en claro salen del <see cref="ArrayPool{T}"/> y se devuelven limpiados.</para>
/// <para><b>Subclave por parte (decisión del usuario, 2026-10-07).</b> Cada parte (y cada reintento de la misma parte)
/// cifra con su propia clave AES-256, derivada con HKDF-SHA256 (RFC 5869) de la DEK y de una sal aleatoria de 32 B que
/// viaja en la cabecera. La clave derivada vive en un búfer propio que se pone a cero en un <c>finally</c>, igual que la
/// DEK.</para>
/// <para><b>Formato FLZ1</b> (enteros big-endian):</para>
/// <code>
/// cabecera   = "FLZ1" (4 B, ASCII) ‖ sal (32 B aleatorios por parte y por intento)          → 36 B
/// bloque_i   = len (uint32, 4 B) ‖ ciphertext (len B) ‖ tag GCM (16 B)
/// archivo    = cabecera ‖ bloque_0 ‖ … ‖ bloque_n          (n ≥ 0: siempre hay al menos un bloque)
/// clave_parte = HKDF-SHA256(ikm = DEK, salt = sal, info = "FLZ1" ‖ lote_id (16 B) ‖ part_number (int32, 4 B), L = 32)
/// nonce_i    = 0x00000000 (4 B) ‖ i (uint64, 8 B)          → 12 B
/// AAD_i      = lote_id (16 B, GUID en orden RFC 4122 big-endian) ‖ part_number (int32, 4 B) ‖ i (uint64, 8 B)
///              ‖ is_final (1 B: 0x01 en el último bloque, 0x00 en los demás)   → 29 B
/// </code>
/// <para><b>Por qué el nonce no lleva prefijo.</b> La unicidad del par (clave, nonce) ya está garantizada por
/// construcción: la clave es única por parte e intento (sal de 256 bits; además <c>lote_id</c> y <c>part_number</c> van
/// en el <c>info</c>) y dentro de una clave el contador no se repite. Un prefijo aleatorio no añadiría seguridad y sí
/// 4 bytes de cabecera y una segunda fuente aleatoria que validar, así que se fija a cero: es la opción más simple.</para>
/// <para>Reglas de framing: los bloques no finales llevan exactamente 1 MiB en claro; el final lleva entre 0 y 1 MiB
/// (un archivo vacío es un único bloque final de longitud 0). Al descifrar, el bloque final es el que va seguido del
/// fin del stream: se verifica con <c>is_final = 1</c>, así que quitar bloques del final, añadir bytes detrás o
/// reordenar (el contador entra en el nonce y en el AAD) hace fallar el tag. Cambiar <c>lote_id</c>,
/// <c>part_number</c> o un bit de la sal también (cambian la subclave). Una cabecera de menos de 36 B (sal incompleta)
/// se rechaza antes de derivar nada. <c>len</c> no va en el AAD, pero alterarlo desplaza el corte del ciphertext y el tag
/// falla; además <c>len &gt; 1 MiB</c> se rechaza antes de leer (sin reservar memoria controlada por el archivo).</para>
/// <para><b>Tag.</b> Lo verifica <see cref="AesGcm.Decrypt(ReadOnlySpan{byte}, ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte}, ReadOnlySpan{byte})"/>
/// (CNG en Windows, OpenSSL en Linux) en tiempo constante; esta clase no compara tags a mano. Si falla, la BCL
/// limpia el búfer de salida y aquí se vuelve a limpiar: nunca se escribe en claro un bloque no autenticado.</para>
/// </summary>
internal sealed class ConsolidadoLoteCipher : IConsolidadoLoteCipher
{
    internal const string Proposito = "Flit.Tramites.ConsolidadoLote.Dek.v1";
    internal const int TamanoDek = 32;
    internal const int TamanoBloque = 1024 * 1024;
    internal const int TamanoTag = 16;
    internal const int TamanoSal = 32;
    internal const int TamanoClaveParte = 32;
    internal const int TamanoNonce = 12;
    internal const int TamanoCabecera = 4 + TamanoSal;
    internal const int TamanoLongitud = 4;
    internal const int TamanoAad = 29;
    internal const int TamanoInfo = 24;

    private static ReadOnlySpan<byte> Magia => "FLZ1"u8;

    private readonly IDataProtector _protector;

    public ConsolidadoLoteCipher(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Proposito);
    }

    public byte[] GenerarDekEnvuelta()
    {
        var dek = RandomNumberGenerator.GetBytes(TamanoDek);
        try
        {
            return _protector.Protect(dek);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<ConsolidadoLoteCifradoResultado> CifrarAsync(
        byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenEnClaro, Stream destinoCifrado, CancellationToken ct = default)
    {
        var dek = Desenvolver(dekEnvuelta);
        try
        {
            // Sal nueva en cada llamada: un reintento de la misma parte cifra con otra subclave.
            var sal = RandomNumberGenerator.GetBytes(TamanoSal);
            return await CifrarConDekAsync(dek, sal, loteId, partNumber, origenEnClaro, destinoCifrado, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<long> DescifrarAsync(
        byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenCifrado, Stream destinoEnClaro, CancellationToken ct = default)
    {
        var dek = Desenvolver(dekEnvuelta);
        try
        {
            return await DescifrarConDekAsync(dek, loteId, partNumber, origenCifrado, destinoEnClaro, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    /// <summary>Núcleo del cifrado con la DEK en claro y una sal dada (los vectores fijos lo usan).</summary>
    internal static async Task<ConsolidadoLoteCifradoResultado> CifrarConDekAsync(
        byte[] dek, byte[] sal, Guid loteId, int partNumber, Stream origen, Stream destino, CancellationToken ct)
    {
        ValidarArgumentos(dek, loteId, partNumber, origen, destino);
        if (sal is not { Length: TamanoSal })
            throw new ArgumentException($"La sal debe tener {TamanoSal} bytes.", nameof(sal));
        if (!origen.CanRead || !destino.CanWrite)
            throw new ArgumentException("El origen debe ser legible y el destino escribible.");

        using var aes = CrearAesDeLaParte(dek, sal, loteId, partNumber);
        var actual = ArrayPool<byte>.Shared.Rent(TamanoBloque);
        var siguiente = ArrayPool<byte>.Shared.Rent(TamanoBloque);
        var cifrado = ArrayPool<byte>.Shared.Rent(TamanoBloque);
        var marco = new byte[TamanoLongitud + TamanoTag];
        try
        {
            var cabecera = new byte[TamanoCabecera];
            Magia.CopyTo(cabecera);
            sal.CopyTo(cabecera, Magia.Length);
            await destino.WriteAsync(cabecera, ct).ConfigureAwait(false);

            long enClaro = 0, escritos = TamanoCabecera;
            ulong contador = 0;
            var largoActual = await LeerBloqueAsync(origen, actual, ct).ConfigureAwait(false);
            while (true)
            {
                // Lectura adelantada: un bloque es final si viene corto o si no hay nada detrás.
                var largoSiguiente = 0;
                var esFinal = largoActual < TamanoBloque;
                if (!esFinal)
                {
                    largoSiguiente = await LeerBloqueAsync(origen, siguiente, ct).ConfigureAwait(false);
                    esFinal = largoSiguiente == 0;
                }

                Sellar(aes, loteId, partNumber, contador, esFinal, actual, largoActual, cifrado, marco);
                await destino.WriteAsync(marco.AsMemory(0, TamanoLongitud), ct).ConfigureAwait(false);
                await destino.WriteAsync(cifrado.AsMemory(0, largoActual), ct).ConfigureAwait(false);
                await destino.WriteAsync(marco.AsMemory(TamanoLongitud, TamanoTag), ct).ConfigureAwait(false);
                enClaro += largoActual;
                escritos += TamanoLongitud + largoActual + TamanoTag;

                if (esFinal)
                    return new ConsolidadoLoteCifradoResultado(enClaro, escritos, (long)contador + 1);

                (actual, siguiente) = (siguiente, actual);
                largoActual = largoSiguiente;
                contador++;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(actual, clearArray: true);
            ArrayPool<byte>.Shared.Return(siguiente, clearArray: true);
            ArrayPool<byte>.Shared.Return(cifrado, clearArray: false);
        }
    }

    /// <summary>Núcleo del descifrado con la DEK en claro (los vectores fijos lo usan).</summary>
    internal static async Task<long> DescifrarConDekAsync(
        byte[] dek, Guid loteId, int partNumber, Stream origen, Stream destino, CancellationToken ct)
    {
        ValidarArgumentos(dek, loteId, partNumber, origen, destino);
        if (!origen.CanRead || !destino.CanWrite)
            throw new ArgumentException("El origen debe ser legible y el destino escribible.");

        var cabecera = new byte[TamanoCabecera];
        if (await origen.ReadAtLeastAsync(cabecera, TamanoCabecera, throwOnEndOfStream: false, ct).ConfigureAwait(false) < TamanoCabecera
            || !cabecera.AsSpan(0, Magia.Length).SequenceEqual(Magia))
            throw Corrupta(); // cabecera truncada (incluida una sal incompleta) o magia ajena

        using var aes = CrearAesDeLaParte(dek, cabecera.AsSpan(Magia.Length, TamanoSal), loteId, partNumber);
        var cifrado = ArrayPool<byte>.Shared.Rent(TamanoBloque + TamanoTag);
        var enClaro = ArrayPool<byte>.Shared.Rent(TamanoBloque);
        var longitud = new byte[TamanoLongitud];
        try
        {
            // Sin bloques no hay archivo válido: el cifrador siempre emite al menos el bloque final.
            if (!await LeerLongitudAsync(origen, longitud, ct).ConfigureAwait(false))
                throw Corrupta();
            var largo = LongitudValida(longitud);

            long escritos = 0;
            ulong contador = 0;
            while (true)
            {
                if (await origen.ReadAtLeastAsync(cifrado.AsMemory(0, largo + TamanoTag), largo + TamanoTag, throwOnEndOfStream: false, ct)
                        .ConfigureAwait(false) < largo + TamanoTag)
                    throw Corrupta(); // bloque cortado

                // El bloque es final si detrás no hay nada (ni un byte).
                var esFinal = !await LeerLongitudAsync(origen, longitud, ct).ConfigureAwait(false);
                var largoSiguiente = esFinal ? 0 : LongitudValida(longitud);
                if (!esFinal && largo != TamanoBloque)
                    throw Corrupta(); // solo el bloque final puede venir corto

                Abrir(aes, loteId, partNumber, contador, esFinal, cifrado, largo, enClaro);
                await destino.WriteAsync(enClaro.AsMemory(0, largo), ct).ConfigureAwait(false);
                escritos += largo;

                if (esFinal)
                    return escritos;
                largo = largoSiguiente;
                contador++;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(cifrado, clearArray: false);
            ArrayPool<byte>.Shared.Return(enClaro, clearArray: true);
        }
    }

    private static void Sellar(
        AesGcm aes, Guid loteId, int partNumber, ulong contador, bool esFinal,
        byte[] claro, int largo, byte[] cifrado, byte[] marco)
    {
        Span<byte> nonce = stackalloc byte[TamanoNonce];
        Span<byte> aad = stackalloc byte[TamanoAad];
        EscribirNonce(nonce, contador);
        EscribirAad(aad, loteId, partNumber, contador, esFinal);
        BinaryPrimitives.WriteUInt32BigEndian(marco.AsSpan(0, TamanoLongitud), (uint)largo);
        aes.Encrypt(nonce, claro.AsSpan(0, largo), cifrado.AsSpan(0, largo), marco.AsSpan(TamanoLongitud, TamanoTag), aad);
    }

    private static void Abrir(
        AesGcm aes, Guid loteId, int partNumber, ulong contador, bool esFinal,
        byte[] cifrado, int largo, byte[] claro)
    {
        Span<byte> nonce = stackalloc byte[TamanoNonce];
        Span<byte> aad = stackalloc byte[TamanoAad];
        EscribirNonce(nonce, contador);
        EscribirAad(aad, loteId, partNumber, contador, esFinal);
        try
        {
            aes.Decrypt(nonce, cifrado.AsSpan(0, largo), cifrado.AsSpan(largo, TamanoTag), claro.AsSpan(0, largo), aad);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(claro.AsSpan(0, largo));
            throw new ConsolidadoLoteCifradoException(ConsolidadoLoteCifradoError.ParteCorrupta, ex);
        }
    }

    /// <summary>
    /// Deriva la subclave AES-256 de la parte y devuelve el <see cref="AesGcm"/> listo. El búfer de la subclave se pone a
    /// cero en el <c>finally</c> (<see cref="AesGcm"/> guarda su propia copia en el handle nativo, que libera al disponerse).
    /// </summary>
    private static AesGcm CrearAesDeLaParte(byte[] dek, ReadOnlySpan<byte> sal, Guid loteId, int partNumber)
    {
        var clave = new byte[TamanoClaveParte];
        try
        {
            DerivarClaveParte(dek, sal, loteId, partNumber, clave);
            return new AesGcm(clave, TamanoTag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clave);
        }
    }

    /// <summary><c>clave_parte = HKDF-SHA256(ikm = DEK, salt = sal, info = "FLZ1" ‖ lote_id ‖ part_number)</c>, 32 B.</summary>
    internal static void DerivarClaveParte(
        ReadOnlySpan<byte> dek, ReadOnlySpan<byte> sal, Guid loteId, int partNumber, Span<byte> destino)
    {
        if (dek.Length != TamanoDek || sal.Length != TamanoSal || destino.Length != TamanoClaveParte)
            throw new ArgumentException("Tamaños de DEK, sal o subclave inválidos.");
        Span<byte> info = stackalloc byte[TamanoInfo];
        EscribirInfo(info, loteId, partNumber);
        HKDF.DeriveKey(HashAlgorithmName.SHA256, dek, destino, sal, info);
    }

    internal static void EscribirInfo(Span<byte> info, Guid loteId, int partNumber)
    {
        Magia.CopyTo(info);
        if (!loteId.TryWriteBytes(info.Slice(4, 16), bigEndian: true, out _))
            throw new InvalidOperationException("No se pudo serializar el id del lote.");
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(20, 4), partNumber);
    }

    /// <summary><c>nonce_i = 0x00000000 ‖ i</c>: con subclave única por parte e intento, el contador basta.</summary>
    internal static void EscribirNonce(Span<byte> nonce, ulong contador)
    {
        nonce[..4].Clear();
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], contador);
    }

    internal static void EscribirAad(Span<byte> aad, Guid loteId, int partNumber, ulong contador, bool esFinal)
    {
        if (!loteId.TryWriteBytes(aad[..16], bigEndian: true, out _))
            throw new InvalidOperationException("No se pudo serializar el id del lote.");
        BinaryPrimitives.WriteInt32BigEndian(aad.Slice(16, 4), partNumber);
        BinaryPrimitives.WriteUInt64BigEndian(aad.Slice(20, 8), contador);
        aad[28] = esFinal ? (byte)1 : (byte)0;
    }

    private byte[] Desenvolver(byte[]? dekEnvuelta)
    {
        if (dekEnvuelta is null || dekEnvuelta.Length == 0)
            throw new ConsolidadoLoteCifradoException(ConsolidadoLoteCifradoError.DekAusente);

        byte[] dek;
        try
        {
            dek = _protector.Unprotect(dekEnvuelta);
        }
        catch (CryptographicException ex)
        {
            throw new ConsolidadoLoteCifradoException(ConsolidadoLoteCifradoError.DekInvalida, ex);
        }

        if (dek.Length != TamanoDek)
        {
            CryptographicOperations.ZeroMemory(dek);
            throw new ConsolidadoLoteCifradoException(ConsolidadoLoteCifradoError.DekInvalida);
        }

        return dek;
    }

    private static void ValidarArgumentos(byte[] dek, Guid loteId, int partNumber, Stream origen, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(dek);
        ArgumentNullException.ThrowIfNull(origen);
        ArgumentNullException.ThrowIfNull(destino);
        if (dek.Length != TamanoDek)
            throw new ArgumentException($"La DEK debe tener {TamanoDek} bytes.", nameof(dek));
        if (loteId == Guid.Empty)
            throw new ArgumentException("El id del lote es obligatorio.", nameof(loteId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partNumber);
    }

    private static async Task<int> LeerBloqueAsync(Stream origen, byte[] bufer, CancellationToken ct) =>
        await origen.ReadAtLeastAsync(bufer.AsMemory(0, TamanoBloque), TamanoBloque, throwOnEndOfStream: false, ct)
            .ConfigureAwait(false);

    /// <summary><c>true</c> si leyó los 4 bytes de longitud; <c>false</c> si el stream terminó justo ahí; corrupta si quedó a medias.</summary>
    private static async Task<bool> LeerLongitudAsync(Stream origen, byte[] longitud, CancellationToken ct)
    {
        var leidos = await origen.ReadAtLeastAsync(longitud, TamanoLongitud, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (leidos == 0)
            return false;
        if (leidos < TamanoLongitud)
            throw Corrupta();
        return true;
    }

    private static int LongitudValida(byte[] longitud)
    {
        var largo = BinaryPrimitives.ReadUInt32BigEndian(longitud);
        if (largo > TamanoBloque)
            throw Corrupta();
        return (int)largo;
    }

    private static ConsolidadoLoteCifradoException Corrupta() =>
        new(ConsolidadoLoteCifradoError.ParteCorrupta);
}
