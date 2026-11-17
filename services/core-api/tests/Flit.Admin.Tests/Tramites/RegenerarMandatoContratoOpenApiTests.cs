using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #14547 — el contrato de «Regenerar mandato y consolidado» está publicado en
/// <c>contracts/openapi/core-api.v1.yaml</c> (el frontend lo consume en paralelo) y el <c>info.version</c> subió de patch.
/// <para>Uso de ejemplo: <c>Path(Yaml(), RutaRegenerar)</c> devuelve el bloque YAML del path.</para>
/// </summary>
public sealed class RegenerarMandatoContratoOpenApiTests
{
    private const string RutaRegenerar = "/api/v1/admin/tramites/{id}/mandato/regenerar";

    [Fact]
    public void Contrato_ElPathEstaPublicado_ConSusCodigosYSoloSuperAdmin()
    {
        var bloque = Path(Yaml(), RutaRegenerar);

        bloque.Should().Contain("    post:\n").And.Contain("operationId: adminTramiteRegenerarMandatoYConsolidado");
        bloque.Should().Contain("TenantIdHeader").And.Contain("SuperAdmin");
        foreach (var status in new[] { "\"200\"", "\"400\"", "\"401\"", "\"403\"", "\"404\"", "\"409\"", "\"503\"" })
            bloque.Should().Contain(status);
        foreach (var codigo in new[] { "estado_no_permite_regenerar_mandato", "migrado_solo_lectura", "organismo_requerido", "storage_unavailable" })
            bloque.Should().Contain(codigo);
        bloque.Should().Contain("#/components/schemas/RegenerarMandatoYConsolidadoResponse");
        bloque.Should().Contain("#/components/schemas/RegenerarMandatoProblem", "el 409 declara el ProblemDetails con `error`");
        Schema(Yaml(), "RegenerarMandatoProblem").Should().Contain("error:").And.Contain("detail:");
    }

    [Fact]
    public void Contrato_ElEsquemaDeRespuestaDeclaraMandatoConsolidadosYAvisos()
    {
        var esquema = Schema(Yaml(), "RegenerarMandatoYConsolidadoResponse");

        foreach (var campo in new[] { "mandato:", "consolidado:", "maestro:", "avisos:", "firmanteAnteriorId:", "firmanteNuevoId:", "sinFirmar:", "generadoEn:", "estado:", "codigo:", "mensaje:" })
            esquema.Should().Contain(campo);
        // El consolidado del gestor también puede fallar tras confirmar el mandato (éxito parcial).
        System.Text.RegularExpressions.Regex.IsMatch(esquema, @"\n        consolidado:\n(?:          .*\n)*?            estado:")
            .Should().BeTrue("consolidado declara su propio `estado`");
        foreach (var valor in new[] { "regenerado", "radicado_conservado", "fallo", "maestro_radicado_conservado", "maestro_fallo", "mandato_sin_firmar", "mandato_personalizado_conservado", "mandato_personalizado_aplicado", "consolidado_fallo" })
            esquema.Should().Contain(valor);
    }

    [Fact]
    public void Contrato_InfoVersionSubioDePatch()
    {
        Regex.Match(Yaml(), "^  version: \"(?<v>[^\"]+)\"", RegexOptions.Multiline).Groups["v"].Value
            .Should().Be("1.1.4", "cambiar el contrato sube el patch de info.version");
    }

    private static string Yaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "contracts", "openapi", "core-api.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("el contrato OpenAPI debe encontrarse desde el directorio de tests");
        return File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "contracts", "openapi", "core-api.v1.yaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Path(string yaml, string ruta)
    {
        var m = Regex.Match(yaml, "^  " + Regex.Escape(ruta) + ":\\n((?:(?:    .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"paths.{ruta} debe existir en core-api.v1.yaml");
        return m.Groups[1].Value;
    }

    private static string Schema(string yaml, string nombre)
    {
        var m = Regex.Match(yaml, "^    " + nombre + ":\\n((?:(?:      .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"components.schemas.{nombre} debe existir en core-api.v1.yaml");
        return m.Groups[1].Value;
    }
}
