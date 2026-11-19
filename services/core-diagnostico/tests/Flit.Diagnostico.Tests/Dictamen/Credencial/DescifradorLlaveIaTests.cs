using System.Security.Cryptography;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen.Credencial;

/// <summary>
/// ADR-0091 §2 (sobre a), SP-9 y SP-11: el sobre RSA-OAEP-256 + AES-256-GCM se abre con la privada de Diagnóstico y el AAD canónico
/// (con <c>modelo</c> tras <c>proveedor</c>). El vector fijo es el mismo que consume core-api: la clave es SOLO de prueba.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --sin-bd --filter "FullyQualifiedName~DescifradorLlaveIaTests"
/// </code>
/// </example>
public sealed class DescifradorLlaveIaTests
{
    private static readonly VectorIa V = VectorIa.Cargar();

    private static byte[] AadDelVector(string? tool = null, string? proveedor = null, string? modelo = null, long? rowVersion = null,
        string? kid = null, byte[]? nonce = null, long? emitida = null) =>
        SobreLlaveIa.ComponerAad(
            tool ?? V.Texto("tool_code"),
            proveedor ?? V.Texto("proveedor"),
            modelo ?? V.Texto("modelo"),
            rowVersion ?? V.Numero("row_version"),
            kid ?? V.Kid,
            nonce ?? V.Nonce,
            emitida ?? V.EmitidaSegundos);

    [Fact]
    public void SP11_ElAadCanonicoCoincideByteAByteConElVector()
    {
        var aad = AadDelVector();

        aad.Should().Equal(V.Bytes("aad_b64"));
        Encoding.UTF8.GetString(aad).Should().Be(V.Texto("aad_utf8"));
        Encoding.UTF8.GetString(aad).Split('\n').Should().Equal(
            "flit.ia.llave.v1", "diagnostico", "diagnostico_dictamen", "anthropic", "claude-haiku-4-5", "7", V.Kid, V.Texto("nonce_b64url"), "1795000000");
        SobreLlaveIa.Alg.Should().Be(V.Texto("alg"));
    }

    [Fact]
    public void SP9_ElSobreDelVectorSeAbreConLaPrivadaDePrueba()
    {
        using var privada = V.Privada();
        privada.KeySize.Should().Be(3072);

        var llave = DescifradorLlaveIa.Descifrar(V.Sobre(), V.Kid, privada, AadDelVector());

        llave.Should().Be(V.Texto("llave_en_claro"));
    }

    [Fact]
    public void SP9_ConCekEIvFijos_AesGcmDaElMismoTextoCifradoYTagDelVector()
    {
        var claro = Encoding.UTF8.GetBytes(V.Texto("llave_en_claro"));
        var cifrado = new byte[claro.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(V.Bytes("cek_b64"), 16))
            aes.Encrypt(V.Bytes("iv_b64"), claro, cifrado, tag, AadDelVector());

        cifrado.Should().Equal(V.Sobre().TextoCifrado.ToByteArray());
        tag.Should().Equal(V.Sobre().Tag.ToByteArray());
        using var privada = V.Privada();
        privada.Decrypt(V.Sobre().CekCifrada.ToByteArray(), RSAEncryptionPadding.OaepSHA256).Should().Equal(V.Bytes("cek_b64"));
    }

    public static TheoryData<string> CamposDelAad => ["tool", "proveedor", "modelo", "row_version", "kid", "nonce", "emitida_en"];

    [Theory]
    [MemberData(nameof(CamposDelAad))]
    public void SP11_MutarCualquierCampoDelAad_FallaElTag(string campo)
    {
        var nonce = V.Nonce;
        nonce[0] ^= 0x01;
        var aad = campo switch
        {
            "tool" => AadDelVector(tool: "ocr"),
            "proveedor" => AadDelVector(proveedor: "openai"),
            "modelo" => AadDelVector(modelo: "claude-sonnet-4-5"),
            "row_version" => AadDelVector(rowVersion: 8),
            "kid" => AadDelVector(kid: "diag-prueba-202612"),
            "nonce" => AadDelVector(nonce: nonce),
            "emitida_en" => AadDelVector(emitida: V.EmitidaSegundos + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(campo)),
        };
        using var privada = V.Privada();

        var acto = () => DescifradorLlaveIa.Descifrar(V.Sobre(), V.Kid, privada, aad);

        acto.Should().Throw<AuthenticationTagMismatchException>(campo);
    }

    [Theory]
    [InlineData("texto_cifrado")]
    [InlineData("tag")]
    [InlineData("iv")]
    [InlineData("cek_cifrada")]
    public void SP9_UnBitCambiadoEnElSobre_NoSeAbre(string campo)
    {
        var sobre = V.Sobre();
        static ByteString Voltear(ByteString b)
        {
            var x = b.ToByteArray();
            x[^1] ^= 0x80;
            return ByteString.CopyFrom(x);
        }

        switch (campo)
        {
            case "texto_cifrado": sobre.TextoCifrado = Voltear(sobre.TextoCifrado); break;
            case "tag": sobre.Tag = Voltear(sobre.Tag); break;
            case "iv": sobre.Iv = Voltear(sobre.Iv); break;
            case "cek_cifrada": sobre.CekCifrada = Voltear(sobre.CekCifrada); break;
        }

        using var privada = V.Privada();

        var acto = () => DescifradorLlaveIa.Descifrar(sobre, V.Kid, privada, AadDelVector());

        acto.Should().Throw<CryptographicException>(campo);
    }

    [Theory]
    [InlineData("iv", 11)]
    [InlineData("iv", 16)]
    [InlineData("tag", 15)]
    [InlineData("tag", 12)]
    [InlineData("cek_cifrada", 383)]
    [InlineData("cek_cifrada", 256)]
    [InlineData("texto_cifrado", 0)]
    public void SP9_TamanosDistintosDelV1_SeRechazanSinDescifrar(string campo, int largo)
    {
        var sobre = V.Sobre();
        var bytes = ByteString.CopyFrom(new byte[largo]);
        switch (campo)
        {
            case "iv": sobre.Iv = bytes; break;
            case "tag": sobre.Tag = bytes; break;
            case "cek_cifrada": sobre.CekCifrada = bytes; break;
            case "texto_cifrado": sobre.TextoCifrado = bytes; break;
        }

        using var privada = V.Privada();

        var acto = () => DescifradorLlaveIa.Descifrar(sobre, V.Kid, privada, AadDelVector());

        acto.Should().Throw<SobreLlaveIaInvalidoException>().Which.Motivo.Should().Be(MotivoSobreInvalido.Tamano);
    }

    [Theory]
    [InlineData("RSA-OAEP+A256GCM")]
    [InlineData("RSA1_5+A256GCM")]
    [InlineData("")]
    public void SP9_AlgDistintoDelUnicoV1_SeRechaza(string alg)
    {
        var sobre = V.Sobre();
        sobre.Alg = alg;
        using var privada = V.Privada();

        var acto = () => DescifradorLlaveIa.Descifrar(sobre, V.Kid, privada, AadDelVector());

        acto.Should().Throw<SobreLlaveIaInvalidoException>().Which.Motivo.Should().Be(MotivoSobreInvalido.AlgDesconocido);
    }

    [Fact]
    public void SP9_KidDelSobreDistintoDelPedido_SeRechaza()
    {
        var sobre = V.Sobre();
        sobre.Kid = "diag-otro-202611";
        using var privada = V.Privada();

        var acto = () => DescifradorLlaveIa.Descifrar(sobre, V.Kid, privada, AadDelVector());

        acto.Should().Throw<SobreLlaveIaInvalidoException>().Which.Motivo.Should().Be(MotivoSobreInvalido.KidDistinto);
    }

    [Theory]
    [InlineData("kid", "Diag-Mayus")]
    [InlineData("kid", "diag\nx")]
    [InlineData("kid", "")]
    [InlineData("kid", "diag-x\n")]
    [InlineData("tool", "diagnostico_dictamen\n")]
    [InlineData("tool", "otra_herramienta")]
    [InlineData("proveedor", "anthropic\nopenai")]
    [InlineData("proveedor", "mistral")]
    [InlineData("modelo", "claude\nhaiku")]
    [InlineData("modelo", "")]
    [InlineData("modelo", "claude-haiku-4-5\n")]
    public void SP11_CamposConSaltoDeLineaOFueraDeCatalogo_NoComponenAad(string campo, string valor)
    {
        var acto = campo switch
        {
            "kid" => () => AadDelVector(kid: valor),
            "tool" => () => AadDelVector(tool: valor),
            "proveedor" => () => AadDelVector(proveedor: valor),
            _ => (Func<byte[]>)(() => AadDelVector(modelo: valor)),
        };

        acto.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(0)]
    public void SP12_NonceDistintoDe16Bytes_NoComponeAad(int largo)
    {
        var acto = () => AadDelVector(nonce: new byte[largo]);

        acto.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LosNumerosDelAadVanEnDecimalInvariante()
    {
        var aad = Encoding.UTF8.GetString(AadDelVector(rowVersion: 1234567, emitida: 1795000001));

        aad.Should().Contain("\n1234567\n").And.EndWith("\n1795000001");
    }
}
