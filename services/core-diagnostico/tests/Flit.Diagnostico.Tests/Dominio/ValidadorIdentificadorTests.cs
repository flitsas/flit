using System.Text.Json;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13740 (F02-H2) — el validador de identificadores normaliza (mayúsculas, sin espacios) y clasifica cada identificador
/// como placa de carro, moto, remolque o maquinaria, VIN o inválido. La tabla de casos es la misma que consume el frontend
/// (HU #13745): <c>contracts/fixtures/validacion/identificadores-vehiculo.json</c>, enlazada en el csproj.
/// </summary>
/// <example>
/// <code>
/// ValidadorIdentificador.Validar("  abc 123 ") // → ResultadoValidacion("ABC123", TipoIdentificador.PlacaCarro)
/// ValidadorIdentificador.Validar("ABC123456")  // → ResultadoValidacion("ABC123456", null), EsValido == false
/// </code>
/// </example>
public sealed class ValidadorIdentificadorTests
{
    private static readonly string RutaTabla = Path.Combine(AppContext.BaseDirectory, "Fixtures", "identificadores-vehiculo.json");

    // Escenario 1: el identificador se normaliza antes de clasificar.
    [Fact]
    public void Normaliza_abc_123_con_espacios_alrededor_a_ABC123_antes_de_clasificar()
    {
        var resultado = ValidadorIdentificador.Validar("  abc 123 ");

        resultado.Normalizado.Should().Be("ABC123");
        resultado.Tipo.Should().Be(TipoIdentificador.PlacaCarro);
        ValidadorIdentificador.Normalizar("  abc 123 ").Should().Be("ABC123");
    }

    // Escenario 2: cada identificador recibe su tipo sin intervención del usuario.
    [Theory]
    [InlineData("ABC123", TipoIdentificador.PlacaCarro)]
    [InlineData("ABC12D", TipoIdentificador.PlacaMoto)]
    [InlineData("ABC12", TipoIdentificador.PlacaMoto)]
    [InlineData("R12345", TipoIdentificador.PlacaRemolque)]
    [InlineData("MC029554", TipoIdentificador.PlacaMaquinaria)]
    [InlineData("9FBTEST1234567890", TipoIdentificador.Vin)]
    public void Detecta_el_tipo_de_placa_y_VIN(string identificador, TipoIdentificador esperado)
    {
        var resultado = ValidadorIdentificador.Validar(identificador);

        resultado.Tipo.Should().Be(esperado);
        resultado.EsValido.Should().BeTrue();
        resultado.Normalizado.Should().Be(identificador);
    }

    // Escenario 3: un VIN con la letra O o un valor de 9 caracteres queda INVALIDO.
    [Theory]
    [InlineData("9FBTEST12345678O9")]
    [InlineData("ABC123456")]
    public void VIN_con_O_o_valor_de_9_caracteres_queda_invalido(string identificador)
    {
        var resultado = ValidadorIdentificador.Validar(identificador);

        resultado.Tipo.Should().BeNull();
        resultado.EsValido.Should().BeFalse();
        Codigo(resultado).Should().Be("INVALIDO");
    }

    [Fact]
    public void Nulo_queda_invalido_con_normalizado_vacio()
    {
        var resultado = ValidadorIdentificador.Validar(null);

        resultado.Normalizado.Should().BeEmpty();
        resultado.EsValido.Should().BeFalse();
    }

    // Escenario 4: cada caso del archivo compartido da el resultado esperado.
    [Theory]
    [MemberData(nameof(Casos))]
    public void Cada_caso_de_la_tabla_compartida_da_el_resultado_esperado(string entrada, string normalizado, string tipo, bool valido)
    {
        var resultado = ValidadorIdentificador.Validar(entrada);

        resultado.Normalizado.Should().Be(normalizado);
        Codigo(resultado).Should().Be(tipo);
        resultado.EsValido.Should().Be(valido);
    }

    // DoD: la tabla no se adelgaza ni pierde tipos sin que falle un test.
    [Fact]
    public void La_tabla_tiene_al_menos_20_casos_y_los_seis_tipos()
    {
        var casos = LeerCasos();
        var seisTipos = Enum.GetValues<TipoIdentificador>().Select(t => JsonNamingPolicy.SnakeCaseUpper.ConvertName(t.ToString()))
            .Append(ResultadoValidacion.CodigoInvalido).ToList();

        casos.Should().HaveCountGreaterThanOrEqualTo(20);
        seisTipos.Should().HaveCount(6);
        casos.Select(c => c.Tipo).Distinct().Should().BeEquivalentTo(seisTipos);
        casos.Should().OnlyContain(c => c.Valido == (c.Tipo != ResultadoValidacion.CodigoInvalido));
    }

    public static TheoryData<string, string, string, bool> Casos()
    {
        var datos = new TheoryData<string, string, string, bool>();
        foreach (var c in LeerCasos())
            datos.Add(c.Entrada, c.Normalizado, c.Tipo, c.Valido);
        return datos;
    }

    private static string Codigo(ResultadoValidacion r) =>
        r.Tipo is { } t ? JsonNamingPolicy.SnakeCaseUpper.ConvertName(t.ToString()) : ResultadoValidacion.CodigoInvalido;

    private static List<Caso> LeerCasos()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RutaTabla));
        return doc.RootElement.GetProperty("casos").EnumerateArray()
            .Select(c => new Caso(
                c.GetProperty("entrada").GetString()!,
                c.GetProperty("normalizado").GetString()!,
                c.GetProperty("tipo").GetString()!,
                c.GetProperty("valido").GetBoolean()))
            .ToList();
    }

    private sealed record Caso(string Entrada, string Normalizado, string Tipo, bool Valido);
}
