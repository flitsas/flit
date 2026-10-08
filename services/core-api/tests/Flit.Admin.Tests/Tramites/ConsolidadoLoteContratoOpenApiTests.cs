using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// Épica #13216 (HU #13374, AC4) — el contrato del motor de lotes está publicado en
/// <c>contracts/openapi/core-api.v1.yaml</c> (contract-first: el frontend #13381/#13382 se construyó contra él):
/// los cinco paths del diseño con <c>x-required-permission</c>, los esquemas del cuerpo y la respuesta, los códigos
/// estables (incluido <c>motor_inactivo</c>) y el <c>POST /api/v1/tramites/instances/search</c> con
/// <c>TramitesSearchFilter</c>, que el lote reutiliza.
/// <para>Uso de ejemplo: <c>Path(yaml, "/api/v1/tramites/consolidados/lotes")</c> devuelve el bloque YAML del path.</para>
/// </summary>
public sealed class ConsolidadoLoteContratoOpenApiTests
{
    private const string Permiso = "x-required-permission: consolidado-masivo.download";

    public static TheoryData<string, string, string> PathsDelLote => new()
    {
        { "/api/v1/tramites/consolidados/lotes", "post", "CrearLoteConsolidados" },
        { "/api/v1/consolidados/lotes/actual", "get", "ObtenerLoteConsolidadosActual" },
        { "/api/v1/consolidados/lotes/{loteId}", "get", "ObtenerLoteConsolidados" },
        { "/api/v1/consolidados/lotes/{loteId}/partes/{numero}", "get", "DescargarParteLoteConsolidados" },
        { "/api/v1/consolidados/lotes/{loteId}/cancelacion", "post", "CancelarLoteConsolidados" },
    };

    [Theory]
    [MemberData(nameof(PathsDelLote))]
    public void AC4_CadaPathDelMotorEstaPublicadoConSuOperacionYElPermiso(string ruta, string verbo, string operationId)
    {
        var bloque = Path(Yaml(), ruta);

        bloque.Should().Contain($"    {verbo}:\n", $"{ruta} debe declarar {verbo.ToUpperInvariant()}");
        bloque.Should().Contain($"operationId: {operationId}");
        bloque.Should().Contain(Permiso, $"{ruta} exige el permiso del motor (D7)");
    }

    [Fact]
    public void AC4_LaCancelacionQuedaMarcadaParaLaFeature13307()
    {
        Path(Yaml(), "/api/v1/consolidados/lotes/{loteId}/cancelacion").Should().Contain("#13307");
    }

    [Fact]
    public void AC4_ElPostDeCreacion_Publica202_409_422_503_YLosCodigosEstables()
    {
        var bloque = Path(Yaml(), "/api/v1/tramites/consolidados/lotes");

        foreach (var status in new[] { "\"202\"", "\"400\"", "\"401\"", "\"403\"", "\"409\"", "\"422\"", "\"503\"" })
            bloque.Should().Contain(status);
        foreach (var codigo in new[]
                 {
                     "confirmacion_requerida", "tipo_no_permitido", "seleccion_requerida", "seleccion_invalida",
                     "filtro_invalido", "sin_compania", "lote_activo", "seleccion_excede_tope",
                     "busqueda_demasiado_amplia", "motor_inactivo", "lote_no_creado",
                 })
            bloque.Should().Contain(codigo, $"el código estable {codigo} debe estar documentado en el POST");
        bloque.Should().Contain("X-Tenant-Id");
        bloque.Should().Contain("#/components/schemas/CrearLoteConsolidadosRequest");
        bloque.Should().Contain("#/components/schemas/LoteConsolidados");
        bloque.Should().Contain("#/components/schemas/LoteActivoConflict");
    }

    [Fact]
    public void AC4_LosEsquemasDelLoteEstanPublicados()
    {
        var yaml = Yaml();

        Schema(yaml, "CrearLoteConsolidadosRequest").Should().Contain("confirmaEfectos").And.Contain("seleccion");
        var seleccion = Schema(yaml, "LoteSeleccionRequest");
        seleccion.Should().Contain("modo").And.Contain("ids").And.Contain("excluidos").And.Contain("filtro");
        Schema(yaml, "LoteFiltroTramites").Should().Contain("alcanceRed").And.Contain("TramitesSearchFilter");
        Schema(yaml, "LoteActivoConflict").Should().Contain("loteActivoId");
        var lote = Schema(yaml, "LoteConsolidados");
        foreach (var campo in new[] { "estado", "tipoDocumento", "total", "procesados", "incluidos", "omitidos", "creadoEn", "partes" })
            lote.Should().Contain(campo + ":");
        lote.Should().Contain("en_cola").And.Contain("expirado");
    }

    /// <summary>
    /// M1 (épica #13216) — el 422 <c>seleccion_excede_tope</c> por tope total lleva <c>total</c> y <c>tope</c>
    /// opcionales en la raíz del ProblemDetails, y el POST documenta el tope configurable.
    /// </summary>
    [Fact]
    public void M1_ElProblemaDelLoteDeclaraTotalYTopeOpcionales_YElPostDocumentaElTopeTotal()
    {
        var yaml = Yaml();

        var problema = Schema(yaml, "LoteProblem");
        problema.Should().Contain("        total:").And.Contain("        tope:");
        problema.Should().Contain("required: [error]", "total y tope son opcionales");
        Path(yaml, "/api/v1/tramites/consolidados/lotes").Should().Contain("max_items_per_batch");
        Path(yaml, "/api/v1/admin/ot/consolidados/lotes").Should().Contain("max_items_per_batch");
    }

    /// <summary>
    /// HU #13379 — las tres GET están implementadas y su contrato documenta el dueño por <c>sub</c>, el 204, la
    /// descarga en streaming con <c>Content-Length</c> y los códigos estables con <c>LoteProblem</c>.
    /// </summary>
    [Fact]
    public void HU13379_LasGetDelLoteDocumentanDuenoStreamingYCodigos()
    {
        var yaml = Yaml();

        var actual = Path(yaml, "/api/v1/consolidados/lotes/actual");
        actual.Should().NotContain("Se implementa en #13379").And.Contain("\"204\"").And.Contain("expiraEn")
            .And.Contain("X-Tenant-Id");
        Path(yaml, "/api/v1/consolidados/lotes/{loteId}").Should().NotContain("Se implementa en #13379")
            .And.Contain("\"404\"").And.Contain("Super Admin");

        var parte = Path(yaml, "/api/v1/consolidados/lotes/{loteId}/partes/{numero}");
        parte.Should().NotContain("Se implementa en #13379");
        foreach (var status in new[] { "\"200\"", "\"403\"", "\"404\"", "\"409\"", "\"410\"", "\"500\"", "\"503\"" })
            parte.Should().Contain(status);
        foreach (var codigo in new[] { "lote_no_terminado", "descarga_expirada", "auditoria_no_registrada", "parte_no_disponible" })
            parte.Should().Contain(codigo);
        parte.Should().Contain("Content-Length").And.Contain("Content-Disposition").And.Contain("application/zip")
            .And.Contain("#/components/schemas/LoteProblem");

        Schema(yaml, "LoteProblem").Should().Contain("- parte_no_disponible");
    }

    /// <summary>
    /// HU #13385 — la cancelación está implementada: <c>CancelarLoteConsolidados</c> con 202/403/404/409/503, el 409 con
    /// <c>LoteTerminadoConflict</c> (<c>error</c> + <c>estado</c> terminal) y el 503 <c>auditoria_no_registrada</c>.
    /// </summary>
    [Fact]
    public void HU13385_LaCancelacionDocumenta202_403_404_409_503_YLoteTerminadoConflict()
    {
        var yaml = Yaml();

        var cancelacion = Path(yaml, "/api/v1/consolidados/lotes/{loteId}/cancelacion");
        cancelacion.Should().NotContain("Ruta reservada").And.Contain("HU #13385");
        foreach (var status in new[] { "\"202\"", "\"403\"", "\"404\"", "\"409\"", "\"503\"" })
            cancelacion.Should().Contain(status);
        cancelacion.Should().Contain("#/components/schemas/LoteConsolidados")
            .And.Contain("#/components/schemas/LoteTerminadoConflict")
            .And.Contain("lote_terminado")
            .And.Contain("auditoria_no_registrada")
            .And.Contain("Super Admin");

        var conflicto = Schema(yaml, "LoteTerminadoConflict");
        conflicto.Should().Contain("required: [error, estado]").And.Contain("enum: [lote_terminado]")
            .And.Contain("enum: [completado, completado_con_omitidos, fallido, expirado]");
        Schema(yaml, "LoteProblem").Should().Contain("- lote_terminado").And.Contain("- auditoria_no_registrada");
    }

    [Fact]
    public void AC4_DocumentaElSearchDeTramites_YTramitesSearchFilterReutilizado()
    {
        var yaml = Yaml();

        var search = Path(yaml, "/api/v1/tramites/instances/search");
        search.Should().Contain("    post:\n").And.Contain("operationId: SearchProcedureInstances");
        search.Should().Contain("#/components/schemas/TramitesSearchRequest");

        var filtro = Schema(yaml, "TramitesSearchFilter");
        foreach (var campo in new[] { "condiciones", "placa", "estado", "busquedaRapida", "createdFrom", "sortBy", "sortDir" })
            filtro.Should().Contain(campo + ":");
        Schema(yaml, "TramitesSearchRequest").Should().Contain("#/components/schemas/TramitesSearchFilter");
        Schema(yaml, "NetworkTramitesSearchRequest").Should().Contain("#/components/schemas/TramitesSearchFilter");
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

    /// <summary>Bloque de <c>paths.&lt;ruta&gt;</c>: desde su clave hasta el siguiente path hermano.</summary>
    private static string Path(string yaml, string ruta)
    {
        var m = Regex.Match(yaml, "^  " + Regex.Escape(ruta) + ":\\n((?:(?:    .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"paths.{ruta} debe existir en core-api.v1.yaml");
        return m.Groups[1].Value;
    }

    /// <summary>Bloque de <c>components.schemas.&lt;nombre&gt;</c>: desde su clave hasta el siguiente schema hermano.</summary>
    private static string Schema(string yaml, string nombre)
    {
        var m = Regex.Match(yaml, "^    " + nombre + ":\\n((?:(?:      .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"components.schemas.{nombre} debe existir en core-api.v1.yaml");
        return m.Groups[1].Value;
    }
}
