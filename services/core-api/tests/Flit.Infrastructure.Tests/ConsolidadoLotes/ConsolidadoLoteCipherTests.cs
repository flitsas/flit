using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flit.Infrastructure.Security;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13372 (épica #13216) — cifrado FLZ1 de las partes del lote: ida y vuelta, manipulación detectada, DEK ausente y
/// vectores fijos del formato.
/// <para>Uso de ejemplo:</para>
/// <code>
/// var cipher = new ConsolidadoLoteCipher(new EphemeralDataProtectionProvider());
/// var dek = cipher.GenerarDekEnvuelta();
/// await cipher.CifrarAsync(dek, loteId, 1, claro, cifrado, ct);
/// await cipher.DescifrarAsync(dek, loteId, 1, cifrado, salida, ct);
/// </code>
/// </summary>
public sealed class ConsolidadoLoteCipherTests
{
    private const int MiB = 1024 * 1024;
    private static readonly Guid Lote = Guid.Parse("6b0f3f6e-2c1a-4d7e-9a55-0d6c1f2b3a40");

    private readonly EphemeralDataProtectionProvider _provider = new();

    private ConsolidadoLoteCipher Cipher() => new(_provider);

    private static byte[] Aleatorio(int n)
    {
        var b = new byte[n];
        Random.Shared.NextBytes(b);
        return b;
    }

    private async Task<byte[]> CifrarAsync(byte[] dekWrapped, byte[] claro, Guid? lote = null, int parte = 1)
    {
        using var destino = new MemoryStream();
        await Cipher().CifrarAsync(dekWrapped, lote ?? Lote, parte, new MemoryStream(claro), destino, TestContext.Current.CancellationToken);
        return destino.ToArray();
    }

    /// <summary>Descifra y devuelve (excepción o null, bytes en claro escritos antes de fallar).</summary>
    private async Task<(ConsolidadoLoteCifradoException? Error, byte[] Salida)> DescifrarAsync(
        byte[]? dekWrapped, byte[] cifrado, Guid? lote = null, int parte = 1)
    {
        using var salida = new MemoryStream();
        try
        {
            await Cipher().DescifrarAsync(dekWrapped, lote ?? Lote, parte, new MemoryStream(cifrado), salida, TestContext.Current.CancellationToken);
            return (null, salida.ToArray());
        }
        catch (ConsolidadoLoteCifradoException ex)
        {
            return (ex, salida.ToArray());
        }
    }

    /// <summary>Offset del inicio (campo len) del bloque <paramref name="i"/> en un archivo de bloques completos.</summary>
    private static int OffsetBloque(int i) => ConsolidadoLoteCipher.TamanoCabecera + (i * (4 + MiB + 16));

    // ── AC1 — Ida y vuelta ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_IdaYVuelta_3MiB_ConDekEnvueltaConDataProtection_EsIdenticoByteAByte()
    {
        var cipher = Cipher();
        var dekWrapped = cipher.GenerarDekEnvuelta();
        var claro = Aleatorio(3 * MiB);

        using var cifrado = new MemoryStream();
        var r = await cipher.CifrarAsync(dekWrapped, Lote, 2, new MemoryStream(claro), cifrado, TestContext.Current.CancellationToken);
        cifrado.Position = 0;
        using var salida = new MemoryStream();
        var escritos = await cipher.DescifrarAsync(dekWrapped, Lote, 2, cifrado, salida, TestContext.Current.CancellationToken);

        salida.ToArray().Should().Equal(claro);
        escritos.Should().Be(3 * MiB);
        r.BytesEnClaro.Should().Be(3 * MiB);
        r.Bloques.Should().Be(3);
        r.BytesCifrados.Should().Be(cifrado.Length).And.Be(8 + (3 * (4 + MiB + 16)));
        cifrado.ToArray().AsSpan(0, 4).ToArray().Should().Equal("FLZ1"u8.ToArray());
    }

    [Fact]
    public void AC1_Contrato_LaDekSeEnvuelveConElPropositoVersionadoYMide256Bits()
    {
        ConsolidadoLoteCipher.Proposito.Should().Be("Flit.Tramites.ConsolidadoLote.Dek.v1");
        var dekWrapped = Cipher().GenerarDekEnvuelta();

        var dek = _provider.CreateProtector("Flit.Tramites.ConsolidadoLote.Dek.v1").Unprotect(dekWrapped);
        dek.Should().HaveCount(32);
        var otraDek = _provider.CreateProtector("Flit.Tramites.ConsolidadoLote.Dek.v1").Unprotect(Cipher().GenerarDekEnvuelta());
        otraDek.Should().NotEqual(dek, "cada lote tiene su propia DEK aleatoria");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(MiB - 1)]
    [InlineData(MiB)]
    [InlineData(MiB + 1)]
    public async Task AC1_Borde_IdaYVueltaEnLosLimitesDeBloque(int tamano)
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var claro = Aleatorio(tamano);

        var (error, salida) = await DescifrarAsync(dekWrapped, await CifrarAsync(dekWrapped, claro));

        error.Should().BeNull();
        salida.Should().Equal(claro);
    }

    [Fact]
    public async Task AC1_Borde_DosCifradosDelMismoClaroDifierenPorElPrefijoDeNonceAleatorio()
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var claro = Encoding.UTF8.GetBytes("mismo contenido");

        var a = await CifrarAsync(dekWrapped, claro);
        var b = await CifrarAsync(dekWrapped, claro);

        a.AsSpan(4, 4).ToArray().Should().NotEqual(b.AsSpan(4, 4).ToArray());
        a.Should().NotEqual(b);
    }

    // ── AC2 — Manipulación detectada ─────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_QuitarElUltimoBloqueCompleto_FallaSinEscribirElBloqueAfectado()
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var claro = Aleatorio(3 * MiB);
        var cifrado = await CifrarAsync(dekWrapped, claro);

        var (error, salida) = await DescifrarAsync(dekWrapped, cifrado[..OffsetBloque(2)]);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        // El bloque 1 pasa a ser el último pero se cifró con is_final = 0: su tag falla y no se escribe.
        salida.Should().Equal(claro[..MiB]);
    }

    [Fact]
    public async Task AC2_CortarBytesDelUltimoBloque_FallaSinEscribirElBloqueAfectado()
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var claro = Aleatorio(3 * MiB);
        var cifrado = await CifrarAsync(dekWrapped, claro);

        var (error, salida) = await DescifrarAsync(dekWrapped, cifrado[..^10]);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        salida.Should().Equal(claro[..(2 * MiB)]);
    }

    [Fact]
    public async Task AC2_ReordenarDosBloques_FallaSinDevolverBytesEnClaro()
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var claro = Aleatorio(3 * MiB);
        var cifrado = await CifrarAsync(dekWrapped, claro);
        var largo = 4 + MiB + 16;
        var reordenado = (byte[])cifrado.Clone();
        cifrado.AsSpan(OffsetBloque(1), largo).CopyTo(reordenado.AsSpan(OffsetBloque(0)));
        cifrado.AsSpan(OffsetBloque(0), largo).CopyTo(reordenado.AsSpan(OffsetBloque(1)));

        var (error, salida) = await DescifrarAsync(dekWrapped, reordenado);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        salida.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_DescifrarConElAadDeOtroLote_FallaSinDevolverBytesEnClaro()
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var cifrado = await CifrarAsync(dekWrapped, Aleatorio(MiB + 100));

        var (error, salida) = await DescifrarAsync(dekWrapped, cifrado, lote: Guid.Parse("6b0f3f6e-2c1a-4d7e-9a55-0d6c1f2b3a41"));

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        salida.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_DescifrarConElAadDeOtraParte_FallaSinDevolverBytesEnClaro()
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var cifrado = await CifrarAsync(dekWrapped, Aleatorio(MiB + 100), parte: 1);

        var (error, salida) = await DescifrarAsync(dekWrapped, cifrado, parte: 2);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        salida.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_Borde_BytesAnadidosTrasElBloqueFinal_Falla()
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var cifrado = await CifrarAsync(dekWrapped, Aleatorio(500));
        var extendido = cifrado.Concat(cifrado[8..]).ToArray(); // repite el bloque final detrás

        var (error, salida) = await DescifrarAsync(dekWrapped, extendido);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        salida.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bit-ciphertext")]
    [InlineData("bit-tag")]
    [InlineData("prefijo-nonce")]
    [InlineData("magia")]
    [InlineData("solo-cabecera")]
    [InlineData("len-gigante")]
    [InlineData("len-cortado")]
    public async Task AC2_Borde_AlteracionesDelFraming_FallanConErrorControlado(string caso)
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var cifrado = await CifrarAsync(dekWrapped, Aleatorio(300));
        byte[] alterado = caso switch
        {
            "solo-cabecera" => cifrado[..8],
            "len-cortado" => cifrado[..10],
            _ => (byte[])cifrado.Clone(),
        };
        switch (caso)
        {
            case "bit-ciphertext": alterado[8 + 4 + 5] ^= 0x01; break;
            case "bit-tag": alterado[^1] ^= 0x80; break;
            case "prefijo-nonce": alterado[5] ^= 0x01; break;
            case "magia": alterado[3] = (byte)'2'; break;
            case "len-gigante": BinaryPrimitives.WriteUInt32BigEndian(alterado.AsSpan(8, 4), uint.MaxValue); break;
        }

        var (error, salida) = await DescifrarAsync(dekWrapped, alterado);

        error.Should().NotBeNull();
        error!.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        error.Message.Should().NotContain(Lote.ToString());
        salida.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_Borde_BloqueNoFinalCorto_SeRechazaAunqueElTagSeaValido()
    {
        // Contrato del framing: solo el bloque final puede venir con menos de 1 MiB.
        var dek = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var prefijo = new byte[] { 1, 2, 3, 4 };
        using var falso = new MemoryStream();
        falso.Write("FLZ1"u8);
        falso.Write(prefijo);
        using var aes = new AesGcm(dek, 16);
        for (ulong i = 0; i < 2; i++)
        {
            var claro = new byte[10];
            var ct = new byte[10];
            var tag = new byte[16];
            var nonce = new byte[12];
            var aad = new byte[29];
            ConsolidadoLoteCipher.EscribirNonce(nonce, prefijo, i);
            ConsolidadoLoteCipher.EscribirAad(aad, Lote, 1, i, esFinal: i == 1);
            aes.Encrypt(nonce, claro, ct, tag, aad);
            var len = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, 10);
            falso.Write(len);
            falso.Write(ct);
            falso.Write(tag);
        }

        falso.Position = 0;
        using var salida = new MemoryStream();
        var act = () => ConsolidadoLoteCipher.DescifrarConDekAsync(dek, Lote, 1, falso, salida, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ConsolidadoLoteCifradoException>()).Which.Error.Should().Be(ConsolidadoLoteCifradoError.ParteCorrupta);
        salida.Length.Should().Be(0);
    }

    // ── AC3 — Sin clave no hay descifrado ────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC3_DekWrappedNullOVacia_FallaConErrorControladoDekAusente(bool nula)
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var cifrado = await CifrarAsync(dekWrapped, Aleatorio(1000));

        var (error, salida) = await DescifrarAsync(nula ? null : [], cifrado);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.DekAusente);
        error.Message.Should().Be("El lote no tiene clave de cifrado (purgado o cancelado).");
        salida.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_CifrarSinDek_FallaConErrorControladoYNoEscribeNada()
    {
        using var destino = new MemoryStream();
        var act = () => Cipher().CifrarAsync(null, Lote, 1, new MemoryStream([1, 2, 3]), destino, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ConsolidadoLoteCifradoException>()).Which.Error.Should().Be(ConsolidadoLoteCifradoError.DekAusente);
        destino.Length.Should().Be(0);
    }

    [Theory]
    [InlineData("otro-proposito")]
    [InlineData("alterada")]
    [InlineData("otro-keyring")]
    public async Task AC3_Borde_DekEnvueltaNoDesenvolvible_FallaConDekInvalida(string caso)
    {
        var dekWrapped = Cipher().GenerarDekEnvuelta();
        var cifrado = await CifrarAsync(dekWrapped, Aleatorio(1000));
        var invalida = caso switch
        {
            "otro-proposito" => _provider.CreateProtector("Flit.Tramites.ConsolidadoLote.Dek.v2").Protect(new byte[32]),
            "otro-keyring" => new EphemeralDataProtectionProvider().CreateProtector(ConsolidadoLoteCipher.Proposito).Protect(new byte[32]),
            _ => dekWrapped.Select((b, i) => i == dekWrapped.Length - 1 ? (byte)(b ^ 1) : b).ToArray(),
        };

        var (error, salida) = await DescifrarAsync(invalida, cifrado);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.DekInvalida);
        salida.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_Borde_DekDesenvueltaDeLongitudIncorrecta_FallaConDekInvalida()
    {
        var corta = _provider.CreateProtector(ConsolidadoLoteCipher.Proposito).Protect(new byte[16]);

        var (error, _) = await DescifrarAsync(corta, [0x46, 0x4c, 0x5a, 0x31, 0, 0, 0, 0]);

        error!.Error.Should().Be(ConsolidadoLoteCifradoError.DekInvalida);
    }

    // ── AC5 — Vectores fijos FLZ1 ────────────────────────────────────────────────────────
    // Generados con una implementación INDEPENDIENTE (Python `cryptography` 48.0, AESGCM) a partir de la
    // especificación del XML doc de ConsolidadoLoteCipher, no con este código. Si alguno cambia, el formato cambió:
    // eso exige FLZ2, no regenerar el vector.

    private static readonly byte[] VectorDek = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] VectorPrefijo = Convert.FromHexString("a1b2c3d4");
    private static readonly Guid VectorLote = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
    private const int VectorParte = 3;

    private const string VectorVacio = "464c5a31a1b2c3d400000000aa8c06a9b31b1404bee509e6ce4306e6";

    private const string VectorTexto =
        "464c5a31a1b2c3d400000026c44f7cbcbcd88f5e3aaba5cbbe6f277823f6e4b6b86380fd4d7a4460d99a13a8ed50eb3ec09c43d1e3b4"
        + "9e96ef68c03edf4eae46735d";

    private const string VectorTextoClaro = "FLIT FLZ1 vector de prueba — parte 3";

    /// <summary>Patrón determinista del vector multibloque: byte i = (31·i + 7) mod 256.</summary>
    private static byte[] Patron(int n) => Enumerable.Range(0, n).Select(i => (byte)((i * 31) + 7)).ToArray();

    [Theory]
    [InlineData(VectorVacio, "")]
    [InlineData(VectorTexto, VectorTextoClaro)]
    public async Task AC5_VectoresFijos_SeDescifranAlTextoEnClaroEsperado(string cifradoHex, string claroEsperado)
    {
        using var salida = new MemoryStream();
        await ConsolidadoLoteCipher.DescifrarConDekAsync(
            VectorDek, VectorLote, VectorParte, new MemoryStream(Convert.FromHexString(cifradoHex)), salida,
            TestContext.Current.CancellationToken);

        Encoding.UTF8.GetString(salida.ToArray()).Should().Be(claroEsperado);
    }

    [Theory]
    [InlineData(VectorVacio, "")]
    [InlineData(VectorTexto, VectorTextoClaro)]
    public async Task AC5_VectoresFijos_ElCifradoConPrefijoFijoReproduceLosBytesExactos(string cifradoHex, string claro)
    {
        using var destino = new MemoryStream();
        await ConsolidadoLoteCipher.CifrarConDekAsync(
            VectorDek, VectorPrefijo, VectorLote, VectorParte, new MemoryStream(Encoding.UTF8.GetBytes(claro)), destino,
            TestContext.Current.CancellationToken);

        Convert.ToHexStringLower(destino.ToArray()).Should().Be(cifradoHex);
    }

    [Theory]
    [InlineData(MiB + 7, 1048631, "83e344e759831b7438e7dfd9cb3ad0c77747bf3fa19b0d440867f960ec64eaf4",
        "6d02564acf71afa21459aae6388bcbd1,517aa87bfb841cebd27a744b393cee09")]
    [InlineData(MiB, 1048604, "f0bb91b5f1f1beccaafb819f09104807800fa41f034f34a0c53250afe8b3ebac",
        "40669d8d33329b48a64ea517f7f812bd")]
    public async Task AC5_VectoresFijosMultibloque_HashYTagsExactosYDescifradoAlPatron(
        int tamano, int largoCifrado, string sha256Cifrado, string tagsCsv)
    {
        var claro = Patron(tamano);
        using var destino = new MemoryStream();
        await ConsolidadoLoteCipher.CifrarConDekAsync(
            VectorDek, VectorPrefijo, VectorLote, VectorParte, new MemoryStream(claro), destino, TestContext.Current.CancellationToken);
        var cifrado = destino.ToArray();

        cifrado.Should().HaveCount(largoCifrado);
        Convert.ToHexStringLower(SHA256.HashData(cifrado)).Should().Be(sha256Cifrado);
        var tags = tagsCsv.Split(',');
        for (var i = 0; i < tags.Length; i++)
        {
            var finTag = i == tags.Length - 1 ? cifrado.Length : OffsetBloque(i + 1);
            Convert.ToHexStringLower(cifrado.AsSpan(finTag - 16, 16)).Should().Be(tags[i], $"tag del bloque {i}");
        }

        using var salida = new MemoryStream();
        await ConsolidadoLoteCipher.DescifrarConDekAsync(
            VectorDek, VectorLote, VectorParte, new MemoryStream(cifrado), salida, TestContext.Current.CancellationToken);
        salida.ToArray().Should().Equal(claro);
    }

    [Fact]
    public void AC5_Contrato_LayoutDelAadYDelNonce()
    {
        Span<byte> aad = stackalloc byte[29];
        Span<byte> nonce = stackalloc byte[12];
        ConsolidadoLoteCipher.EscribirAad(aad, VectorLote, VectorParte, 2, esFinal: true);
        ConsolidadoLoteCipher.EscribirNonce(nonce, VectorPrefijo, 2);

        Convert.ToHexStringLower(aad).Should().Be("0f1e2d3c4b5a69788796a5b4c3d2e1f0" + "00000003" + "0000000000000002" + "01");
        Convert.ToHexStringLower(nonce).Should().Be("a1b2c3d4" + "0000000000000002");
    }
}
