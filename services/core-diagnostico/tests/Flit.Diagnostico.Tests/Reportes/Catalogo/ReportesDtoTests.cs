using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Saneamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-2 (SP-16; contrato-api §3 y §4) — lista blanca cerrada: el JSON de cada tipo se recorre entero; las columnas y las
/// claves de cada fila son exactamente las de la lista del tipo, en su orden, y no aparece ningún id interno, seudónimo ni nombre de
/// persona en ninguna profundidad. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReportesDtoTests
/// </code>
/// </example>
public sealed class ReportesDtoTests
{
    private static readonly Dictionary<string, string[]> ListaBlanca = new()
    {
        ["vehiculos"] =
        [
            "fecha_diagnostico", "diagnostico_nombre", "modalidad", "tipo_analisis", "tipo_tramite", "placa", "vin", "servicio", "organismo",
            "departamento", "propietarios_documentos", "propietario_declarado_documento", "cambio_propietario", "locatario_runt_documento",
            "veredicto", "estado_consultas", "saneamiento_estado", "tramite_numero",
        ],
        ["consultas"] =
        [
            "fecha_diagnostico", "diagnostico_nombre", "placa", "fuente", "estado", "motivo_na", "tipo_error", "intentos", "duracion_ms",
            "dato_obtenido_en", "reutilizada",
        ],
        ["saneamientos"] =
        [
            "solicitado_en", "placa", "diagnostico_nombre", "estado", "tomado_en", "cerrado_en", "dias_hasta_cierre", "motivo_cierre",
            "renglones_total", "renglones_pendientes",
        ],
        ["derivaciones"] = ["fecha", "placa", "diagnostico_nombre", "tipo_operacion", "tipo_tramite", "estado", "motivo", "tramite_numero"],
    };

    private static readonly string[] Pagina = ["tipo", "ambito", "pagina", "tamano", "total", "acceso_id", "columnas", "filas"];

    [Fact]
    public async Task BE2_cada_tipo_sale_con_la_lista_blanca_exacta_y_sin_ids_ni_nombres()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await ReportesDePrueba.LoteAsync(empresa,
            [new("RPT301", Propietarios: [new("CC", "4444444444")], Locatario: new("CC", "5555555555"),
                PropietarioDeclarado: new("CC", "4444444444"), CambioPropietario: false), new("RPT302")], ct);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[0].Id, EstadoSaneamiento.EnGestion, ct);
        await ReportesDePrueba.DerivacionAsync(empresa, lote.Id, lote.Vehiculos[1].Id, EstadoItemDerivacion.Omitido, ct, motivo: "SIN_DICTAMEN");
        using var cliente = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));

        foreach (var (tipo, claves) in ListaBlanca)
        {
            var respuesta = await cliente.GetAsync(ReportesDePrueba.Ruta(tipo), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, $"{tipo}: {await respuesta.Content.ReadAsStringAsync(ct)}");
            var pagina = await ReportesDePrueba.JsonAsync(respuesta, ct);
            pagina.EnumerateObject().Select(p => p.Name).Should().Equal(Pagina, tipo);
            ReportesDePrueba.Claves(pagina).Should().Equal(claves, $"{tipo}: columnas de la lista blanca en su orden");
            foreach (var columna in pagina.GetProperty("columnas").EnumerateArray())
            {
                columna.EnumerateObject().Select(p => p.Name).Should().Equal("clave", "titulo", "tipo", "pii");
                columna.GetProperty("tipo").GetString().Should().BeOneOf("texto", "numero", "fecha", "fecha_hora", "moneda", "porcentaje");
            }

            var filas = ReportesDePrueba.Filas(pagina);
            filas.Should().NotBeEmpty(tipo);
            foreach (var fila in filas)
                fila.EnumerateObject().Select(p => p.Name).Should().Equal(claves, $"{tipo}: cada fila trae exactamente las claves de columnas");
            Prohibidas(pagina).Should().BeEmpty($"{tipo}: SP-16, ningún id interno, seudónimo ni nombre de persona");
        }

        var vehiculos = await ReportesDePrueba.JsonAsync(await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos"), ct), ct);
        vehiculos.GetProperty("columnas").EnumerateArray().Where(c => c.GetProperty("pii").GetBoolean()).Select(c => c.GetProperty("clave").GetString())
            .Should().BeEquivalentTo("placa", "vin", "propietarios_documentos", "propietario_declarado_documento", "locatario_runt_documento");
        vehiculos.GetProperty("columnas").EnumerateArray().Single(c => c.GetProperty("clave").GetString() == "cambio_propietario")
            .GetProperty("titulo").GetString().Should().Be("Cambio de propietario", "HU #14582 §1.3: rótulo de la columna");
    }

    private static List<string> Prohibidas(JsonElement elemento)
    {
        var halladas = new List<string>();
        void Recorrer(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name.StartsWith("usuario", StringComparison.Ordinal) || p.Name is "persona_id" or "consulta_id" or "tenant_id" or "sub" or "nombre"
                        || p.Name.Contains("nombre_persona", StringComparison.Ordinal) || p.Name.Contains("propietario_nombre", StringComparison.Ordinal))
                    {
                        halladas.Add(p.Name);
                    }

                    Recorrer(p.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray())
                    Recorrer(x);
            }
        }

        Recorrer(elemento);
        return halladas;
    }
}
