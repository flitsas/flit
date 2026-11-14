using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Contratos;

/// <summary>
/// HU #14563 AC43 (CF-30) — <c>contracts/openapi/diagnostico.v1.yaml</c> tras el rediseño: <c>info.version</c> 1.3.1 (la 1.3.0 con la ruptura
/// del PUT del backoffice anotada (B-PR6: <c>motivo</c> requerido, el cuerpo legado sin motivo se retiró), y los cambios del cierre
/// declarados (AC23 <c>ajuste_con_tramite</c> en la auditoría, <c>href</c> en plural); la 1.3.1, la decisión D23 del SuperAdmin).
/// Lectura de texto: el repo no tiene parser
/// YAML en las pruebas.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~OpenApiDiagnosticoContratoTests
/// </code>
/// </example>
public sealed class OpenApiDiagnosticoContratoTests
{
    private static readonly Lazy<string> Yaml = new(() =>
        File.ReadAllText(Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "..", "..", "contracts", "openapi", "diagnostico.v1.yaml"))
            .ReplaceLineEndings("\n"));

    [Fact]
    public void HU14563_AC43_version_1_3_0_con_la_ruptura_del_PUT_del_backoffice_anotada()
    {
        var info = Bloque("info:", sangria: 0);

        info.Should().Contain("version: \"1.4.0\"", "HU #14576 sube a 1.4.0, aditivo");
        info.Should().Contain("Versión 1.3.1").And.Contain("backoffice_diagnostico").And.Contain("SuperAdmin");
        info.Should().Contain("1.3.0").And.Contain("/backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen").And.Contain("motivo");
        Bloque("CuerpoOverrideDictamen:", sangria: 4).Should().Contain("required: [veredicto, motivo, version_esperada]", "B-PR6 retiró el cuerpo legado");
    }

    // HU #14563 code review menores 2, 4 y 10 — el contrato dice lo que hace el código: version_esperada obligatoria en todo PUT,
    // claves desconocidas 422 VALOR_INVALIDO y el ETag de la bandeja con el hash de los nombres de los lotes de la página.
    [Fact]
    public void HU14563_code_review_version_esperada_claves_desconocidas_y_ETag_con_lotes()
    {
        var put = Bloque("put:", sangria: 4, desde: "operationId: corregirDictamenVehiculoBackoffice");
        put.Should().Contain("VALOR_INVALIDO").And.Contain("clave desconocida");
        put.Should().Contain("VERSION_ESPERADA_INVALIDA (campo version_esperada) si falta");

        var bandeja = Bloque("get:", sangria: 4, desde: "operationId: bandejaNotificaciones");
        bandeja.Should().Contain("<hash de los nombres de los lotes>");
    }

    [Fact]
    public void HU14563_AC43_cambios_del_cierre_declarados_en_el_contrato()
    {
        var version = Bloque("VersionDictamen:", sangria: 4);
        version.Should().Contain("ajuste_con_tramite:", "AC23: la auditoría registra el ajuste confirmado con trámite");
        version[..version.IndexOf("properties:", StringComparison.Ordinal)].Should().Contain("ajuste_con_tramite");

        var accion = Bloque("AccionNotificacion:", sangria: 4);
        accion.Should().Contain("/diagnosticos/{diagnostico_id}").And.Contain("example: /diagnosticos/");
        accion.Should().NotContain("/diagnostico/{diagnostico_id}");
    }

    /// <summary>Rutas del saneamiento en esta rama (HU #14576): las del cliente (BE-A), las de deuda y placas (BE-C) y las del backoffice (BE-B).</summary>
    private static readonly string[] RutasSaneamiento =
    [
        "/diagnosticos/{id}/vehiculos/{vid}/saneamiento:",
        "/diagnosticos/{id}/vehiculos/{vid}/saneamiento/renglones/{rid}/evidencia:",
        "/saneamientos/solicitudes:",
        "/diagnosticos/{id}/vehiculos/{vid}/deuda:",
        "/diagnosticos/{id}/vehiculos/{vid}/deuda/pdf:",
        "/diagnosticos/{id}/vehiculos/{vid}/deuda/excel:",
        "/vehiculos/deuda/excel:",
        "/vehiculos:",
        "/backoffice/saneamientos:",
        "/backoffice/saneamientos/resumen:",
        "/backoffice/saneamientos/excel:",
        "/backoffice/saneamientos/tomar:",
        "/backoffice/saneamientos/{sid}:",
        "/backoffice/saneamientos/{sid}/deuda:",
        "/backoffice/saneamientos/{sid}/tomar:",
        "/backoffice/saneamientos/{sid}/observacion:",
        "/backoffice/saneamientos/{sid}/renglones:",
        "/backoffice/saneamientos/{sid}/renglones/{rid}:",
        "/backoffice/saneamientos/{sid}/renglones/{rid}/evidencia:",
        "/backoffice/saneamientos/{sid}/cerrar:",
        "/backoffice/saneamientos/{sid}/reabrir:",
        "/backoffice/saneamientos/{sid}/anular:",
    ];

    // HU #14576 AC51 — 1.4.0 aditivo: todas las rutas del saneamiento, los campos nuevos de la grilla y de la sesión, y los esquemas
    // con los nombres que ya usa el front (D-19, D-20, D-22, D-26).
    [Fact]
    public void HU14576_AC51_version_1_4_0_con_las_rutas_y_esquemas_del_saneamiento()
    {
        var info = Bloque("info:", sangria: 0);
        info.Should().Contain("Versión 1.4.0").And.Contain("aditivo");
        var paths = Bloque("paths:", sangria: 0);
        foreach (var ruta in RutasSaneamiento)
            paths.Split('\n').Should().Contain("  /api/v1/diagnostico" + ruta, $"el contrato declara {ruta}");

        Bloque("ListaBandejaSaneamientos:", sangria: 4).Should().Contain("saneamientos:", "D-19");
        Bloque("ListadoVehiculos:", sangria: 4).Should().Contain("vehiculos:", "D-19");
        Bloque("SaneamientoBackoffice:", sangria: 4).Should().Contain("usuario_id").And.NotContain("solicitado_por_mi", "D-22");
        Bloque("RenglonSaneamiento:", sangria: 4).Should().NotContain("referencia").And.NotContain("sha256", "D-22");
        Bloque("PersonaDeuda:", sangria: 4).Should().NotContain("nombre:", "SP-11, BE-C-1");
        Bloque("SaneamientoFilaBackoffice:", sangria: 4).Should().Contain("id:").And.NotContain("elegible");
        Bloque("SaneamientoFila:", sangria: 4).Should().Contain("elegible:").And.Contain("motivo_no_elegible:");
        Yaml.Value.Should().Contain("TEXTO_INVALIDO").And.Contain("VERSION_DESACTUALIZADA").And.Contain("VERSION_INVALIDA")
            .And.Contain("DEMASIADOS_REEMPLAZOS").And.Contain("ARCHIVO_NO_ES_PDF").And.Contain("DEMASIADAS_FILAS").And.Contain("NO_EDITABLE");
        Yaml.Value.Should().NotContain("CON_ANALISIS_IA", "C2: el modo se serializa SIMPLE | CON_ANALISIS");
        Bloque("Capacidades:", sangria: 4).Should().Contain("sanear:").And.Contain("gestionarSaneamiento:");
    }

    /// <summary>Desde la línea <paramref name="cabecera"/> con esa sangría hasta la siguiente clave del mismo nivel.</summary>
    /// <param name="desde">Si viene, la cabecera es la última anterior a la primera línea que contiene este texto.</param>
    private static string Bloque(string cabecera, int sangria, string? desde = null)
    {
        var prefijo = new string(' ', sangria);
        var lineas = Yaml.Value.Split('\n');
        var ancla = desde is null ? -1 : Array.FindIndex(lineas, l => l.Contains(desde, StringComparison.Ordinal));
        var inicio = ancla < 0
            ? Array.FindIndex(lineas, l => l == prefijo + cabecera)
            : Array.FindLastIndex(lineas, ancla, l => l == prefijo + cabecera);
        inicio.Should().BeGreaterThanOrEqualTo(0, $"el contrato declara {cabecera}");
        var fin = inicio + 1;
        while (fin < lineas.Length && (lineas[fin].Length == 0 || lineas[fin].StartsWith(prefijo + " ", StringComparison.Ordinal)))
            fin++;
        return string.Join('\n', lineas[inicio..fin]);
    }
}
