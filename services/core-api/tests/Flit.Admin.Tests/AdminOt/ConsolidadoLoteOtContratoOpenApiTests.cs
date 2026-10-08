using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.AdminOt;

/// <summary>
/// Épica #13216 (HU #13391) — el contrato de la creación del lote de maestros desde la bandeja OT está publicado en
/// <c>contracts/openapi/core-api.v1.yaml</c> (fragmento §5 del diseño de #13308) y la búsqueda de la bandeja
/// (<c>POST /api/v1/admin/ot/client-procedures/search</c>, deriva FB-l) queda documentada con <c>allOf</c> sobre
/// <c>OtBandejaSearchFilter</c>, el mismo esquema que usa el filtro del lote.
/// <para>Uso de ejemplo: <c>Path(yaml, "/api/v1/admin/ot/consolidados/lotes")</c> devuelve el bloque YAML del path.</para>
/// </summary>
public sealed class ConsolidadoLoteOtContratoOpenApiTests
{
    [Fact]
    public void ElPostDelLoteOt_EstaPublicado_ConPermiso_TransitOfficeId_YLosCodigos()
    {
        var bloque = Path(Yaml(), "/api/v1/admin/ot/consolidados/lotes");

        bloque.Should().Contain("    post:\n");
        bloque.Should().Contain("operationId: CrearLoteConsolidadosMaestrosOt");
        bloque.Should().Contain("x-required-permission: consolidado-masivo.download");
        bloque.Should().Contain("name: transitOfficeId");
        bloque.Should().Contain("#/components/schemas/CrearLoteConsolidadosOtRequest");
        bloque.Should().Contain("#/components/schemas/LoteConsolidados");
        bloque.Should().Contain("#/components/schemas/LoteActivoConflict");
        foreach (var status in new[] { "\"202\"", "\"400\"", "\"401\"", "\"403\"", "\"404\"", "\"409\"", "\"422\"", "\"503\"" })
            bloque.Should().Contain(status);
        foreach (var codigo in new[]
                 {
                     "transit_office_requerido", "transit_office_invalido", "sin_organismo", "tipo_no_permitido",
                     "filtro_invalido", "confirmacion_requerida", "seleccion_excede_tope", "lote_activo", "lote_no_creado",
                 })
            bloque.Should().Contain(codigo, $"el código estable {codigo} debe estar documentado");
    }

    [Fact]
    public void LosEsquemasDelLoteOt_SoloMaestro_YFiltroDeLaBandeja()
    {
        var yaml = Yaml();

        var request = Schema(yaml, "CrearLoteConsolidadosOtRequest");
        request.Should().Contain("confirmaEfectos").And.Contain("seleccion").And.Contain("enum: [consolidado_maestro]");
        request.Should().Contain("#/components/schemas/LoteSeleccionOtRequest");
        Schema(yaml, "LoteSeleccionOtRequest").Should().Contain("modo").And.Contain("excluidos").And.Contain("ids")
            .And.Contain("#/components/schemas/OtBandejaSearchFilter");
        var filtro = Schema(yaml, "OtBandejaSearchFilter");
        foreach (var campo in new[] { "condiciones", "busqueda", "status", "familia", "placa", "createdFrom", "sortBy", "sortDir" })
            filtro.Should().Contain(campo + ":");
        Schema(yaml, "LoteProblem").Should().Contain("transit_office_requerido").And.Contain("sin_organismo");
    }

    [Fact]
    public void LaBusquedaDeLaBandeja_SeDocumentaConAllOfSobreOtBandejaSearchFilter()
    {
        var yaml = Yaml();

        var search = Path(yaml, "/api/v1/admin/ot/client-procedures/search");
        search.Should().Contain("    post:\n").And.Contain("#/components/schemas/OtBandejaSearchRequest");
        var request = Schema(yaml, "OtBandejaSearchRequest");
        request.Should().Contain("allOf").And.Contain("#/components/schemas/OtBandejaSearchFilter").And.Contain("pageSize");
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
