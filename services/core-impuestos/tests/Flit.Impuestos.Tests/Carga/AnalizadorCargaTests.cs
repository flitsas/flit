using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Ejecucion;
using Flit.Impuestos.Tests.Liquidaciones;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Carga;

/// <summary>
/// HU #14694 (M12; F3-CF1, F3-CF2, S4, S16) — <c>POST /liquidaciones/masiva/analizar</c>: lee la columna «placa» de un .csv (separador
/// <c>;</c> o <c>,</c> según el encabezado, UTF-8 con o sin BOM) o de la primera hoja de un .xlsx, normaliza cada placa (S16),
/// cuenta las repetidas contra su primera aparición (S4) y lista las filas inválidas (<c>PLACA_VACIA</c>, <c>PLACA_INVALIDA</c>) con
/// su número de fila. <c>tope</c> es el máximo vigente (operativo si existe; si no, contractual) y <c>superaTope</c> rechaza antes de
/// crear el lote. Formato por contenido (415 <c>FORMATO_NO_SOPORTADO</c>), 5 MB (413) y columna ausente (422). Todo <c>error</c> es el
/// literal de <c>x-flit-literales</c>.
/// </summary>
/// <example>
/// <code>
/// var r = await cliente.PostAsync(EntornoMasiva.Analizar, EntornoMasiva.Archivo(EntornoMasiva.Csv("placa", "ABC123"), "placas.csv"), ct);
/// </code>
/// </example>
public sealed class AnalizadorCargaTests
{
    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];

    [Fact]
    public async Task Csv_con_punto_y_coma_y_BOM_normaliza_deduplica_y_lista_las_filas_invalidas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var archivo = M.CsvConBom("Nombre;Placa", "uno; abc-123 ", "dos;ABC123", "tres;", "cuatro;AB.12", "cinco;xyz 987", "", "seis;AB1");

        var analisis = await AnalizarAsync(api, empresa, archivo, "placas.csv", ct);

        analisis.GetProperty("validas").GetInt32().Should().Be(2, "ABC123 y XYZ987");
        analisis.GetProperty("duplicadas").GetInt32().Should().Be(1, "« abc-123 » y «ABC123» son la misma placa (S16, S4)");
        analisis.GetProperty("tope").GetInt32().Should().Be(500);
        analisis.GetProperty("superaTope").GetBoolean().Should().BeFalse();
        var invalidas = analisis.GetProperty("invalidas").EnumerateArray()
            .Select(i => (i.GetProperty("fila").GetInt32(), i.GetProperty("valor").GetString(), i.GetProperty("motivo").GetString())).ToList();
        invalidas.Should().Equal((4, "", "PLACA_VACIA"), (5, "AB.12", "PLACA_INVALIDA"), (8, "AB1", "PLACA_INVALIDA"));
    }

    [Fact]
    public async Task Csv_con_coma_comillas_y_encabezado_PLACA_sin_BOM()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var archivo = M.Csv("id,\" PLACA \",nota", "1,\"abc-123\",\"con, coma\"", "2,DEF456,", "3,def-456,x");

        var analisis = await AnalizarAsync(api, empresa, archivo, "PLACAS.CSV", ct);

        analisis.GetProperty("validas").GetInt32().Should().Be(2);
        analisis.GetProperty("duplicadas").GetInt32().Should().Be(1);
        analisis.GetProperty("invalidas").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Xlsx_lee_la_columna_placa_de_la_primera_hoja_con_su_numero_de_fila()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var archivo = M.Xlsx([
            ["Id", "Placa"],
            ["1", "abc 123"],
            ["2", "ABC-123"],
            ["3", null],
            ["4", "ÑAB123"],
            ["5", "GHI789"],
        ]);

        var analisis = await AnalizarAsync(api, empresa, archivo, "placas.xlsx", ct);

        analisis.GetProperty("validas").GetInt32().Should().Be(2);
        analisis.GetProperty("duplicadas").GetInt32().Should().Be(1);
        analisis.GetProperty("invalidas").EnumerateArray()
            .Select(i => (i.GetProperty("fila").GetInt32(), i.GetProperty("valor").GetString(), i.GetProperty("motivo").GetString()))
            .Should().Equal((4, "", "PLACA_VACIA"), (5, "ÑAB123", "PLACA_INVALIDA"));
    }

    [Fact]
    public async Task Sin_columna_placa_responde_422_COLUMNA_PLACA_AUSENTE_en_csv_y_en_xlsx()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));

        await LiquidacionIndividualEndpointsTests.EsperarError(
            await cliente.PostAsync(M.Analizar, M.Archivo(M.Csv("vehiculo;nota", "ABC123;x"), "placas.csv"), ct),
            HttpStatusCode.UnprocessableEntity, "COLUMNA_PLACA_AUSENTE", "Falta la columna de placa.");
        await LiquidacionIndividualEndpointsTests.EsperarError(
            await cliente.PostAsync(M.Analizar, M.Archivo(M.Xlsx([["Vehiculo"], ["ABC123"]]), "placas.xlsx"), ct),
            HttpStatusCode.UnprocessableEntity, "COLUMNA_PLACA_AUSENTE", "Falta la columna de placa.");
    }

    [Fact]
    public async Task Con_mas_placas_que_el_tope_vigente_marca_superaTope_y_la_masiva_rechaza_sin_crear_el_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        await M.SembrarConfiguracionAsync(api, empresa, ct, contractual: 10, operativo: 2);
        var archivo = M.Csv("placa", "AAA111", "BBB222", "CCC333", "aaa-111");

        var analisis = await AnalizarAsync(api, empresa, archivo, "placas.csv", ct);

        analisis.GetProperty("validas").GetInt32().Should().Be(3);
        analisis.GetProperty("tope").GetInt32().Should().Be(2, "el operativo manda si existe");
        analisis.GetProperty("superaTope").GetBoolean().Should().BeTrue();

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsync(M.Masiva, M.Archivo(archivo, "placas.csv"), ct);
        var cuerpo = await LiquidacionIndividualEndpointsTests.EsperarError(
            r, HttpStatusCode.UnprocessableEntity, "LIMITE_SUPERADO", "El archivo supera el tope de 2 placas por lote.");
        cuerpo.GetProperty("tope").GetInt32().Should().Be(2);
        (await M.LotesAsync(api, empresa, ct)).Should().Be(0, "el tope se valida antes de crear el lote (F3-CF2)");
    }

    [Theory]
    [InlineData("placas.txt", false)]
    [InlineData("placas", false)]
    [InlineData("placas.xlsx", false)]
    [InlineData("placas.csv", true)]
    public async Task Un_formato_que_no_es_csv_ni_xlsx_por_extension_o_por_contenido_responde_415_FORMATO_NO_SOPORTADO(string nombre, bool zip)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        // .xlsx con texto adentro (no es un zip) o .csv con un zip adentro: el contenido no corresponde a la extensión.
        var contenido = zip ? M.Xlsx([["placa"], ["ABC123"]]) : M.Csv("placa", "ABC123");

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsync(M.Analizar, M.Archivo(contenido, nombre), ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(
            r, HttpStatusCode.UnsupportedMediaType, "FORMATO_NO_SOPORTADO", "Solo se admiten archivos CSV y XLSX.");
    }

    [Fact]
    public async Task Un_archivo_de_mas_de_5_MB_responde_413_ARCHIVO_DEMASIADO_GRANDE()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var grande = new byte[(5 * 1024 * 1024) + 1];
        Array.Fill(grande, (byte)'A');

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsync(M.Analizar, M.Archivo(grande, "placas.csv"), ct);

        await LiquidacionIndividualEndpointsTests.EsperarError(
            r, HttpStatusCode.RequestEntityTooLarge, "ARCHIVO_DEMASIADO_GRANDE", "El archivo supera el máximo de 5 MB.");
    }

    [Fact]
    public async Task Sin_el_permiso_de_crear_responde_403_y_no_analiza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer]))
            .PostAsync(M.Analizar, M.Archivo(M.Csv("placa", "ABC123"), "placas.csv"), ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    internal static async Task<JsonElement> AnalizarAsync(ApiDePrueba api, Guid empresa, byte[] archivo, string nombre, CancellationToken ct)
    {
        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsync(M.Analizar, M.Archivo(archivo, nombre), ct);
        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        await using (var db = api.Contexto(empresa))
            (await db.Set<Liquidacion>().CountAsync(ct)).Should().Be(0, "analizar no crea nada");
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.Clone();
    }
}
