using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen.Credencial;

/// <summary>
/// SP-18 y SP-16: la credencial no es un <c>record</c> (su <c>ToString</c> autogenerado imprimiría la llave), su <c>ToString</c> va
/// redactado y no se serializa a JSON; <see cref="OpcionesIa"/> tampoco expone la privada. Llave centinela <c>sk-ant-test-CENTINELA</c>.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --sin-bd --filter "FullyQualifiedName~CredencialRedactorTests"
/// </code>
/// </example>
public sealed class CredencialRedactorTests
{
    private const string Centinela = "sk-ant-test-CENTINELA";
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    private static CredencialRedactor Credencial() => new("anthropic", "claude-haiku-4-5", Centinela, 7);

    [Fact]
    public void SP18_ToStringVaRedactado()
    {
        var texto = Credencial().ToString();

        texto.Should().NotContain(Centinela).And.NotContain("sk-ant");
        texto.Should().Contain("anthropic").And.Contain("claude-haiku-4-5");
        ResultadoCredencial.Con(Credencial()).ToString().Should().NotContain(Centinela);
        $"{Credencial()}".Should().NotContain(Centinela);
    }

    [Fact]
    public void SP18_ElJsonDeLaCredencialNoTraeLaLlave()
    {
        JsonSerializer.Serialize(Credencial()).Should().NotContain(Centinela);
        JsonSerializer.Serialize(Credencial(), JsonWeb).Should().NotContain(Centinela);
        JsonSerializer.Serialize(ResultadoCredencial.Con(Credencial())).Should().NotContain(Centinela);
    }

    [Fact]
    public void SP18_NoEsRecord_YLaLlaveNoEsPublicaNiSerializable()
    {
        var tipo = typeof(CredencialRedactor);

        tipo.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance).Should().BeNull("un record imprime sus propiedades en ToString");
        tipo.GetMethod("<Clone>$").Should().BeNull();
        tipo.GetProperty("ApiKey", BindingFlags.Public | BindingFlags.Instance).Should().BeNull("la llave no es pública");
        var llave = tipo.GetProperty("ApiKey", BindingFlags.NonPublic | BindingFlags.Instance);
        llave.Should().NotBeNull();
        llave!.GetCustomAttribute<JsonIgnoreAttribute>().Should().NotBeNull();
        tipo.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void SP16_OpcionesIaToString_NoExponeLaPrivada()
    {
        var v = VectorIa.Cargar();
        var ruta = Path.Combine(Path.GetTempPath(), "flit-ia-" + Guid.NewGuid().ToString("N") + ".p8");
        File.WriteAllText(ruta, v.PrivadaBase64);
        try
        {
            var o = new OpcionesIa { Origen = OpcionesIa.OrigenPlataforma, ClavePrivadaArchivo = ruta, Kid = v.Kid };

            var texto = o.ToString();

            texto.Should().NotContain(v.PrivadaBase64[..40]).And.NotContain(ruta);
            texto.Should().Contain("plataforma").And.Contain(v.Kid).And.Contain("presente");
            new OpcionesIa().ToString().Should().Contain("ausente");
        }
        finally
        {
            File.Delete(ruta);
        }
    }
}
