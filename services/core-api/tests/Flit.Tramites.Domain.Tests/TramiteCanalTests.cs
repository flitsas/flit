using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #14317 (Feature #14315, Épica #14296) AC5 — el catálogo de canales es único: <see cref="TramiteCanal"/> trae los
/// cinco códigos con su nombre visible y el enum <c>TramiteCanal</c> de <c>contracts/openapi/core-api.v1.yaml</c> coincide.
/// <para>Uso de ejemplo: <c>TramiteCanal.NombreVisible("pn_punto")</c> devuelve «Persona Natural OT».</para>
/// </summary>
public sealed class TramiteCanalTests
{
    public static TheoryData<string, string> CodigosYNombres => new()
    {
        { "tramites", "Trámites" },
        { "pn_web", "Persona Natural Web" },
        { "pn_punto", "Persona Natural OT" },
        { "integracion", "Integración" },
        { "aplicacion", "Aplicación" },
    };

    [Fact]
    public void AC5_ElCatalogoTieneLosCincoCodigosEnOrden() =>
        TramiteCanal.Todos.Should().Equal("tramites", "pn_web", "pn_punto", "integracion", "aplicacion");

    [Theory]
    [MemberData(nameof(CodigosYNombres))]
    public void AC5_CadaCodigoEsValidoYTieneSuNombreVisible(string codigo, string nombre)
    {
        TramiteCanal.EsValido(codigo).Should().BeTrue();
        TramiteCanal.NombreVisible(codigo).Should().Be(nombre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("web")]
    [InlineData("TRAMITES")]
    [InlineData(" pn_web")]
    public void AC2_UnCodigoFueraDelCatalogoNoEsValidoNiTieneNombre(string? codigo)
    {
        TramiteCanal.EsValido(codigo).Should().BeFalse();
        TramiteCanal.NombreVisible(codigo).Should().BeNull();
    }

    [Fact]
    public void AC5_LasConstantesCoincidenConLaLista() =>
        new[]
        {
            TramiteCanal.Tramites, TramiteCanal.PersonaNaturalWeb, TramiteCanal.PersonaNaturalOt,
            TramiteCanal.Integracion, TramiteCanal.Aplicacion,
        }.Should().BeEquivalentTo(TramiteCanal.Todos);

    [Fact]
    public void AC5_ElEnumDelContratoOpenApiCoincideConLaClaseDeDominio()
    {
        var yaml = LeerContrato();

        var bloque = Regex.Match(yaml, @"^    TramiteCanal:\n((?:(?:      .*)?\n)+)", RegexOptions.Multiline);
        bloque.Success.Should().BeTrue("components.schemas.TramiteCanal debe existir en core-api.v1.yaml");

        var enumLinea = Regex.Match(bloque.Groups[1].Value, @"^      enum: \[(.*)\]\s*$", RegexOptions.Multiline);
        enumLinea.Success.Should().BeTrue("el schema debe declarar `enum: [...]`");

        var codigos = enumLinea.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        codigos.Should().Equal(TramiteCanal.Todos);
    }

    private static string LeerContrato()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "core-api.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("el contrato OpenAPI debe encontrarse desde el directorio de tests");
        return File.ReadAllText(Path.Combine(dir!.FullName, "contracts", "openapi", "core-api.v1.yaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
