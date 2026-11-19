using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Plataforma.Ai.Entrega;
using Flit.Infrastructure.Ai.Entrega;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai.Entrega;

/// <summary>
/// ADR-0091 · SP-9/10/11 — sobre RSA-OAEP-256 + A256GCM de la llave del proveedor y AAD canónico (con <c>modelo</c>).
/// El descifrador de este archivo es de prueba: replica lo que hará core-diagnostico con su privada.
/// </summary>
/// <remarks>Uso de ejemplo: <c>SelladorLlaveIa.Sellar(rsaPublica, "diag-dev-202611", llave, AadLlaveIa.Componer(...))</c>.</remarks>
public sealed class SelladorLlaveIaTests
{
    internal const string Llave = "sk-ant-test-CENTINELA-0123456789";
    internal const string Kid = "diag-dev-202611";
    internal static readonly byte[] Nonce = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
    private const long Emitida = 1_795_000_000;

    private static readonly Lazy<RSA> Par3072 = new(() => RSA.Create(3072));

    internal static RSA Privada => Par3072.Value;

    internal static string PublicaBase64 => Convert.ToBase64String(Par3072.Value.ExportSubjectPublicKeyInfo());

    internal static RSA Publica()
    {
        var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Par3072.Value.ExportSubjectPublicKeyInfo(), out _);
        return rsa;
    }

    internal static byte[] Aad(string audiencia = "diagnostico", string tool = "diagnostico_dictamen", string proveedor = "anthropic",
        string modelo = "claude-haiku-4-5", long rowVersion = 7, string kid = Kid, byte[]? nonce = null, long emitida = Emitida) =>
        AadLlaveIa.Componer(audiencia, tool, proveedor, modelo, rowVersion, kid, nonce ?? Nonce, emitida);

    /// <summary>Descifrador de prueba (lo que hará Diagnóstico).</summary>
    internal static string Descifrar(RSA privada, SobreLlaveIa sobre, byte[] aad)
    {
        var cek = privada.Decrypt(sobre.CekCifrada, RSAEncryptionPadding.OaepSHA256);
        var plano = new byte[sobre.TextoCifrado.Length];
        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(sobre.Iv, sobre.TextoCifrado, sobre.Tag, plano, aad);
        return Encoding.UTF8.GetString(plano);
    }

    [Fact]
    public void Aad_EsLaCadenaCanonicaConModelo_ByteAByte()
    {
        var esperado = "flit.ia.llave.v1\ndiagnostico\ndiagnostico_dictamen\nanthropic\nclaude-haiku-4-5\n7\ndiag-dev-202611\nAQIDBAUGBwgJCgsMDQ4PEA\n1795000000";

        Aad().Should().Equal(Encoding.UTF8.GetBytes(esperado));
    }

    [Theory]
    [InlineData("Diag-DEV", "kid con mayúsculas")]
    [InlineData("diag\ndev", "kid con salto de línea")]
    [InlineData("", "kid vacío")]
    public void Aad_KidInvalido_Lanza(string kid, string motivo) =>
        FluentActions.Invoking(() => Aad(kid: kid)).Should().Throw<ArgumentException>(motivo);

    [Fact]
    public void Aad_CamposFueraDeCatalogoOConSaltoDeLinea_Lanzan()
    {
        FluentActions.Invoking(() => Aad(tool: "herramienta_x")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Aad(proveedor: "otro")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Aad(modelo: "claude\nhaiku")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Aad(nonce: new byte[15])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Aad(audiencia: "diag\nnostico")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sellar_IdaYVuelta_ConPar3072_DevuelveLaLlave_ConLosTamanosDelContrato()
    {
        using var publica = Publica();
        var sobre = SelladorLlaveIa.Sellar(publica, Kid, Llave, Aad());

        sobre.Alg.Should().Be("RSA-OAEP-256+A256GCM");
        sobre.Kid.Should().Be(Kid);
        sobre.CekCifrada.Should().HaveCount(384);
        sobre.Iv.Should().HaveCount(12);
        sobre.Tag.Should().HaveCount(16);
        Encoding.UTF8.GetString(sobre.TextoCifrado).Should().NotContain("CENTINELA");
        Descifrar(Privada, sobre, Aad()).Should().Be(Llave);
    }

    [Fact]
    public void Sellar_DosVeces_UsaCekEIvNuevos()
    {
        using var publica = Publica();
        var a = SelladorLlaveIa.Sellar(publica, Kid, Llave, Aad());
        var b = SelladorLlaveIa.Sellar(publica, Kid, Llave, Aad());

        a.Iv.Should().NotEqual(b.Iv);
        a.CekCifrada.Should().NotEqual(b.CekCifrada);
        a.TextoCifrado.Should().NotEqual(b.TextoCifrado);
    }

    [Fact]
    public void Sellar_ConClaveDe2048_Lanza()
    {
        using var corta = RSA.Create(2048);

        FluentActions.Invoking(() => SelladorLlaveIa.Sellar(corta, Kid, Llave, Aad())).Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Sellador_NoGuardaEstado()
    {
        typeof(SelladorLlaveIa).GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => !f.IsLiteral).Should().BeEmpty("SP-10: sin CEK, sobres ni caché en campos");
    }

    public static TheoryData<string> CamposDelAad => ["audiencia", "tool", "proveedor", "modelo", "row_version", "kid", "nonce", "emitida_en"];

    [Theory]
    [MemberData(nameof(CamposDelAad))]
    public void Descifrar_ConCualquierCampoDelAadManipulado_FallaElTag(string campo)
    {
        using var publica = Publica();
        var sobre = SelladorLlaveIa.Sellar(publica, Kid, Llave, Aad());
        var otroNonce = (byte[])Nonce.Clone();
        otroNonce[0] ^= 0xFF;
        var manipulado = campo switch
        {
            "audiencia" => Aad(audiencia: "comparendos"),
            "tool" => Aad(tool: "ocr"),
            "proveedor" => Aad(proveedor: "openai"),
            "modelo" => Aad(modelo: "claude-sonnet-4-5"),
            "row_version" => Aad(rowVersion: 8),
            "kid" => Aad(kid: "diag-dev-202612"),
            "nonce" => Aad(nonce: otroNonce),
            _ => Aad(emitida: Emitida + 1),
        };

        FluentActions.Invoking(() => Descifrar(Privada, sobre, manipulado)).Should().Throw<AuthenticationTagMismatchException>(campo);
    }

    [Fact]
    public void Descifrar_ConTagOTextoAlterado_Falla()
    {
        using var publica = Publica();
        var sobre = SelladorLlaveIa.Sellar(publica, Kid, Llave, Aad());
        var tag = (byte[])sobre.Tag.Clone();
        tag[0] ^= 1;
        var texto = (byte[])sobre.TextoCifrado.Clone();
        texto[0] ^= 1;

        FluentActions.Invoking(() => Descifrar(Privada, sobre with { Tag = tag }, Aad())).Should().Throw<AuthenticationTagMismatchException>();
        FluentActions.Invoking(() => Descifrar(Privada, sobre with { TextoCifrado = texto }, Aad())).Should().Throw<AuthenticationTagMismatchException>();
    }

    /// <summary>
    /// Vector compartido con core-diagnostico (<c>contracts/proto/flit/platform/v1/testdata/configuracion_ia_vector.json</c>, lo
    /// genera HU-7). Se activa al integrar los carriles: quitar el <c>Skip</c> y, si el esquema del JSON difiere, ajustar los
    /// nombres de las propiedades de abajo. Comprueba AAD byte a byte, texto cifrado y tag con CEK/IV fijos y que la privada del
    /// vector desenvuelve su CEK.
    /// </summary>
    [Fact(Skip = "Vector compartido pendiente: lo crea HU-7 (core-diagnostico); el orquestador lo activa al integrar.")]
    public void VectorCompartido_CoincideConElDeDiagnostico()
    {
        string? ruta = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && ruta is null; dir = dir.Parent)
        {
            var candidato = Path.Combine(dir.FullName, "contracts", "proto", "flit", "platform", "v1", "testdata", "configuracion_ia_vector.json");
            if (File.Exists(candidato))
                ruta = candidato;
        }

        ruta.Should().NotBeNull("falta el vector compartido");
        using var json = JsonDocument.Parse(File.ReadAllText(ruta!));
        var v = json.RootElement;
        string S(string n) => v.GetProperty(n).GetString()!;
        byte[] B(string n) => Convert.FromBase64String(S(n));

        var aad = AadLlaveIa.Componer(S("audiencia"), S("tool_code"), S("proveedor"), S("modelo"), v.GetProperty("row_version").GetInt64(),
            S("kid"), B("nonce"), v.GetProperty("emitida_en").GetInt64());
        aad.Should().Equal(Encoding.UTF8.GetBytes(S("aad")));

        using var privada = RSA.Create();
        privada.ImportPkcs8PrivateKey(B("clave_privada_pkcs8"), out _);
        using var publica = RSA.Create();
        publica.ImportSubjectPublicKeyInfo(privada.ExportSubjectPublicKeyInfo(), out _);
        var sobre = SelladorLlaveIa.SellarCon(publica, S("kid"), S("llave"), aad, B("cek"), B("iv"));

        sobre.TextoCifrado.Should().Equal(B("texto_cifrado"));
        sobre.Tag.Should().Equal(B("tag"));
        privada.Decrypt(B("cek_cifrada"), RSAEncryptionPadding.OaepSHA256).Should().Equal(B("cek"));
    }
}
