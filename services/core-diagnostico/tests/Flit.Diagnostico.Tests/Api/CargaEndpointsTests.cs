using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13749 (F02-H4), escenarios 1 a 11 — <c>GET /plantilla</c> y <c>POST /diagnosticos/{id}/carga</c>: la plantilla trae
/// las tres columnas; el mismo contenido en .xlsx y en .csv da el mismo resumen; .xls responde 422 y .pdf o un .xlsx
/// dañado 415; el resumen trae los conteos, consolida duplicados, informa sin válidos y el límite superado, enmascara el
/// documento del locatario (que no llega a los logs) y trae el estimado solo con ANTES_DE_LANZAR; nada llama a una fuente
/// externa. Postgres real (base efímera de <see cref="ApiDePrueba"/>). Fixtures inventados en <c>Api/Fixtures</c>.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CargaEndpointsTests
/// </code>
/// </example>
public sealed class CargaEndpointsTests
{
    private const string RutaPlantilla = Program.Prefijo + "/plantilla";
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Api", "Fixtures");

    // Escenario 1 (F02-C5) — .xlsx con las tres columnas; sin diagnostico.diagnosticos.crear, 403.
    [Fact]
    public async Task Plantilla_es_un_xlsx_con_las_tres_columnas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(Cabeceras.TokenUsuario(api, Guid.NewGuid())).GetAsync(RutaPlantilla, ct);
        var sinPermiso = await api.Cliente(api.Token(permisos: [Permisos.DiagnosticosVer])).GetAsync(RutaPlantilla, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().EndWith(".xlsx");
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var hoja = zip.GetEntry("xl/worksheets/sheet1.xml");
        hoja.Should().NotBeNull("la plantilla es un libro de Excel con una hoja");
        using var lector = new StreamReader(hoja!.Open());
        var xml = await lector.ReadToEndAsync(ct);
        foreach (var columna in new[] { "Identificador_Vehiculo", "Tipo_Documento_Locatario", "Documento_Locatario" })
            xml.Should().Contain(columna);
        xml.IndexOf("Identificador_Vehiculo", StringComparison.Ordinal).Should()
            .BeLessThan(xml.IndexOf("Tipo_Documento_Locatario", StringComparison.Ordinal), "la obligatoria va primero");
        sinPermiso.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // La plantilla que se descarga se puede subir tal cual: sin filas, el resumen dice que no hay válidos.
        var tabla = LectorXlsx.Leer(bytes);
        tabla.Encabezados.Should().Equal("Identificador_Vehiculo", "Tipo_Documento_Locatario", "Documento_Locatario");
        tabla.Filas.Should().BeEmpty();
    }

    // Escenario 2 (F02-C6) — el mismo contenido en .xlsx y en .csv (punto y coma y coma) da el mismo resumen.
    [Fact]
    public async Task Xlsx_y_csv_con_el_mismo_contenido_dan_el_mismo_resumen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);
        var csvComa = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Fixtures, "carga-mixta.csv")).Replace(';', ','));

        var xlsx = await SubirAsync(cliente, id, Fixture("carga-mixta.xlsx"), "carga-mixta.xlsx", ct);
        var csv = await SubirAsync(cliente, id, Fixture("carga-mixta.csv"), "carga-mixta.csv", ct);
        var coma = await SubirAsync(cliente, id, csvComa, "carga-mixta-coma.csv", ct);

        xlsx.StatusCode.Should().Be(HttpStatusCode.OK, await xlsx.Content.ReadAsStringAsync(ct));
        csv.StatusCode.Should().Be(HttpStatusCode.OK, await csv.Content.ReadAsStringAsync(ct));
        coma.StatusCode.Should().Be(HttpStatusCode.OK, await coma.Content.ReadAsStringAsync(ct));
        var deXlsx = await xlsx.Content.ReadAsStringAsync(ct);
        (await csv.Content.ReadAsStringAsync(ct)).Should().Be(deXlsx);
        (await coma.Content.ReadAsStringAsync(ct)).Should().Be(deXlsx);
        var resumen = JsonDocument.Parse(deXlsx).RootElement;
        resumen.GetProperty("total_filas").GetInt32().Should().Be(9, "la fila 9 está vacía y no cuenta");
        Fila(resumen, 3).GetProperty("documento_locatario").GetString().Should().Be("****4050", "la celda numérica se lee como texto");
        Fila(resumen, 4).GetProperty("tipo_documento_locatario").GetString().Should().Be("NIT", "la cadena en línea se lee");
    }

    // Escenario 3 (F02-C6) — .xls (por extensión o por firma) responde 422 con el mensaje de guardarlo como .xlsx, sin filas.
    [Fact]
    public async Task Xls_responde_422_con_el_mensaje_de_guardarlo_como_xlsx()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);

        foreach (var nombre in new[] { "carga.xls", "renombrado.xlsx" })
        {
            var respuesta = await SubirAsync(cliente, id, Fixture("carga.xls"), nombre, ct);

            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, $"{nombre} → {texto}");
            var json = JsonDocument.Parse(texto).RootElement;
            json.GetProperty("code").GetString().Should().Be("FORMATO_XLS_NO_ADMITIDO");
            json.GetProperty("error").GetString().Should().Be("El formato .xls no se admite: guarde el archivo como .xlsx y vuelva a subirlo");
            texto.Should().NotContain("total_filas", "no se analiza ninguna fila");
        }
    }

    // Escenario 4 (F02-C6) — .pdf, un .xlsx dañado y un .csv que no es texto responden 415 con un mensaje claro.
    [Fact]
    public async Task Pdf_y_xlsx_corrupto_responden_415()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);

        (byte[] Bytes, string Nombre, string Codigo)[] casos =
        [
            (Fixture("carga.pdf"), "carga.pdf", "FORMATO_NO_ADMITIDO"),
            (Fixture("carga.pdf"), "pdf-renombrado.csv", "FORMATO_NO_ADMITIDO"),
            (Fixture("carga-corrupta.xlsx"), "carga-corrupta.xlsx", "ARCHIVO_ILEGIBLE"),
            (Fixture("carga.pdf"), "pdf-renombrado.xlsx", "ARCHIVO_ILEGIBLE"),
            ([0x49, 0x64, 0xFF, 0xFE, 0x0A], "latin.csv", "ARCHIVO_ILEGIBLE"),
        ];
        foreach (var (bytes, nombre, codigo) in casos)
        {
            var respuesta = await SubirAsync(cliente, id, bytes, nombre, ct);

            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType, $"{nombre} → {texto}");
            var json = JsonDocument.Parse(texto).RootElement;
            json.GetProperty("code").GetString().Should().Be(codigo, nombre);
            json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace().And.ContainAny(".xlsx", ".csv");
        }
    }

    // Escenario 5 (F02-C7, F02-C4) — válidos, erróneos y duplicados contados; ninguna fuente externa se invoca.
    [Fact]
    public async Task Resumen_trae_los_tres_conteos_sin_invocar_fuentes_externas()
    {
        var ct = TestContext.Current.CancellationToken;
        var externas = new FuenteExternaQueFalla();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostico:Plataforma:RegistrarManifiesto"] = "false" });
            b.Services.ConfigureHttpClientDefaults(h => h.AddHttpMessageHandler(() => new FuenteExternaQueFalla.Manejador(externas)));
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);
        api.Catalogos.FallaSiempre = StatusCode.Unavailable;
        var catalogoAntes = api.Catalogos.Llamadas;
        var httpAntes = externas.Llamadas;

        var respuesta = await SubirAsync(cliente, id, Fixture("carga-mixta.xlsx"), "carga-mixta.xlsx", ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        externas.Llamadas.Should().Be(httpAntes, "el análisis no llama a ninguna fuente por HTTP");
        api.Catalogos.Llamadas.Should().Be(catalogoAntes, "ni al gRPC de Trámites");
        var resumen = JsonDocument.Parse(texto).RootElement;
        resumen.GetProperty("validos").GetInt32().Should().Be(4);
        resumen.GetProperty("erroneos").GetInt32().Should().Be(4);
        resumen.GetProperty("duplicados").GetInt32().Should().Be(1);
        resumen.GetProperty("puede_continuar").GetBoolean().Should().BeTrue();
        resumen.GetProperty("filas").GetArrayLength().Should().Be(8, "nueve filas leídas, una es duplicado consolidado");
        Errores(Fila(resumen, 6)).Should().Equal("IDENTIFICADOR_INVALIDO");
        Errores(Fila(resumen, 7)).Should().Equal("IDENTIFICADOR_DEMASIADO_LARGO");
        Errores(Fila(resumen, 10)).Should().Equal("IDENTIFICADOR_DEMASIADO_LARGO");
        Fila(resumen, 7).GetProperty("identificador_norm").GetString().Should().Be("AB1234567", "nunca se acepta recortada");
        Fila(resumen, 2).GetProperty("tipo_identificador").GetString().Should().Be("PLACA_CARRO");
        Fila(resumen, 4).GetProperty("tipo_identificador").GetString().Should().Be("VIN");
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM diagnostico.vehiculos").SingleAsync(ct))
            .Should().Be(0, "el análisis no guarda filas");
    }

    // Escenario 6 (F02-C8) — ABC123 dos veces: una sola fila y un duplicado.
    [Fact]
    public async Task Duplicados_se_consolidan_en_una_fila()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);

        var resumen = await ResumenAsync(cliente, id, Csv("ABC123", "abc 123", "XYZ987"), ct);

        resumen.GetProperty("duplicados").GetInt32().Should().Be(1);
        resumen.GetProperty("validos").GetInt32().Should().Be(2);
        var filas = resumen.GetProperty("filas").EnumerateArray().ToList();
        filas.Where(f => f.GetProperty("identificador_norm").GetString() == "ABC123").Should().ContainSingle();
        Fila(resumen, 2).GetProperty("filas_duplicadas").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(3);
    }

    // Escenario 7 (F02-C10) — sin registros válidos: aviso SIN_VALIDOS y no se puede continuar.
    [Fact]
    public async Task Archivo_sin_validos_lo_informa_y_no_permite_continuar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);

        var resumen = await ResumenAsync(cliente, id, Csv("ABC123456", "NOPLACA"), ct);
        var vacio = await ResumenAsync(cliente, id, Csv(), ct);

        foreach (var r in new[] { resumen, vacio })
        {
            r.GetProperty("validos").GetInt32().Should().Be(0);
            r.GetProperty("puede_continuar").GetBoolean().Should().BeFalse();
            Avisos(r).Should().Contain("SIN_VALIDOS");
        }

        resumen.GetProperty("erroneos").GetInt32().Should().Be(2);
    }

    // Escenario 8 (F02-C11) — limite_vehiculos_lote=500 y 501 válidos: el resumen informa que se supera.
    [Fact]
    public async Task Limite_por_lote_superado_lo_informa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa { LimiteVehiculosLote = 500 }, ct);

        var en501 = await ResumenAsync(cliente, id, Csv(Placas(501)), ct);
        var en500 = await ResumenAsync(cliente, id, Csv(Placas(500)), ct);

        en501.GetProperty("validos").GetInt32().Should().Be(501);
        en501.GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(500);
        en501.GetProperty("supera_limite").GetBoolean().Should().BeTrue();
        en501.GetProperty("puede_continuar").GetBoolean().Should().BeFalse();
        Avisos(en501).Should().Contain("LIMITE_SUPERADO");
        en500.GetProperty("supera_limite").GetBoolean().Should().BeFalse();
        en500.GetProperty("puede_continuar").GetBoolean().Should().BeTrue();
    }

    // Escenario 9 (F02-C13) — CC con letras: fila errónea; con forma válida, enmascarado y fuera de los logs.
    [Fact]
    public async Task Documento_del_locatario_se_valida_se_enmascara_y_no_llega_a_los_logs()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new CapturaDeLogs();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<CapturaDeLogs>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);

        var respuesta = await SubirAsync(cliente, id,
            Bytes("Identificador_Vehiculo;Tipo_Documento_Locatario;Documento_Locatario", "ABC123;CC;71A3B2", "XYZ987;cc;7766554433", "DEF456;;55443322"),
            "locatarios.csv", ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        var resumen = JsonDocument.Parse(texto).RootElement;
        Fila(resumen, 2).GetProperty("valida").GetBoolean().Should().BeFalse();
        Errores(Fila(resumen, 2)).Should().Equal("DOCUMENTO_INVALIDO");
        Fila(resumen, 3).GetProperty("valida").GetBoolean().Should().BeTrue();
        Fila(resumen, 3).GetProperty("tipo_documento_locatario").GetString().Should().Be("CC");
        Fila(resumen, 3).GetProperty("documento_locatario").GetString().Should().Be("****4433");
        Errores(Fila(resumen, 4)).Should().Equal("DOCUMENTO_INCOMPLETO");
        texto.Should().NotContain("7766554433").And.NotContain("71A3B2").And.NotContain("55443322");
        logs.Mensajes.Should().NotBeEmpty("la captura de logs funciona");
        logs.Mensajes.Should().NotContain(m => m.Contains("7766554433") || m.Contains("71A3B2") || m.Contains("ABC123") || m.Contains("XYZ987"),
            "el contenido de las filas nunca se escribe en logs");
    }

    // Escenario 10 (F01-C11) — con ANTES_DE_LANZAR el resumen trae el estimado por fuente; con NO_MOSTRAR no.
    [Fact]
    public async Task Estimado_por_fuente_solo_con_ANTES_DE_LANZAR()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var contratadas = new List<Fuente> { Fuente.Runt, Fuente.RuntLocatario };
        var (antes, idAntes) = await LoteAsync(api, new ConfiguracionEmpresa { FuentesHabilitadas = contratadas, EstimadoConsultas = EstimadoConsultas.AntesDeLanzar },
            ct, empresas.A, ["RUNT_LOCATARIO"]);
        var (no, idNo) = await LoteAsync(api, new ConfiguracionEmpresa { FuentesHabilitadas = [.. contratadas], EstimadoConsultas = EstimadoConsultas.NoMostrar },
            ct, empresas.B, ["RUNT_LOCATARIO"]);
        var archivo = Bytes("Identificador_Vehiculo;Tipo_Documento_Locatario;Documento_Locatario", "ABC123;CC;1020304050", "XYZ987;;", "DEF456;;", "MAL;;");

        var conEstimado = JsonDocument.Parse(await (await SubirAsync(antes, idAntes, archivo, "a.csv", ct)).Content.ReadAsStringAsync(ct)).RootElement;
        var sinEstimadoTexto = await (await SubirAsync(no, idNo, archivo, "b.csv", ct)).Content.ReadAsStringAsync(ct);

        var estimado = conEstimado.GetProperty("estimado_consultas").EnumerateArray()
            .ToDictionary(e => e.GetProperty("fuente").GetString()!, e => e.GetProperty("consultas").GetInt32());
        estimado.Should().Equal(new Dictionary<string, int> { ["RUNT"] = 3, ["RUNT_LOCATARIO"] = 1 });
        JsonDocument.Parse(sinEstimadoTexto).RootElement.TryGetProperty("estimado_consultas", out _).Should().BeFalse(sinEstimadoTexto);
    }

    // HU #13796 AC1 (F05-C13, Q7) — con ANTES_DE_LANZAR y 10 vehículos, SIMIT_PROPIETARIO muestra 10 con minimo=true (las
    // personas aún no se conocen) y SIMIT_VEHICULO 10 con minimo=false.
    [Fact]
    public async Task Antes_de_lanzar_las_fuentes_de_persona_marcan_minimo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.SimitVehiculo, Fuente.SimitPropietario],
            EstimadoConsultas = EstimadoConsultas.AntesDeLanzar,
        }, ct, porVehiculo: ["RUNT", "SIMIT_VEHICULO"], porPropietario: ["SIMIT_PROPIETARIO"]);

        var respuesta = await SubirAsync(cliente, id, Csv(Placas(10)), "diez.csv", ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        JsonDocument.Parse(texto).RootElement.GetProperty("estimado_consultas").EnumerateArray()
            .Select(e => $"{e.GetProperty("fuente").GetString()} {e.GetProperty("consultas").GetInt32()} {e.GetProperty("minimo").GetBoolean()}")
            .Should().Equal(["RUNT 10 False", "SIMIT_VEHICULO 10 False", "SIMIT_PROPIETARIO 10 True"]);
    }

    // Escenario 11 (F02-C15, meta supuesta pendiente del PO) — 500 filas en menos de 5 s.
    [Fact]
    public async Task Quinientas_filas_se_analizan_en_menos_de_5_segundos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await LoteAsync(api, new ConfiguracionEmpresa(), ct);
        var archivo = Csv(Placas(500));

        var reloj = Stopwatch.StartNew();
        var respuesta = await SubirAsync(cliente, id, archivo, "quinientas.csv", ct);
        reloj.Stop();

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("validos").GetInt32().Should().Be(500);
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        TestContext.Current.SendDiagnosticMessage($"500 filas analizadas en {reloj.ElapsedMilliseconds} ms");
    }

    // Solo en CREADO (409), cabecera de otra compañía (404), tope de tamaño (413), sin archivo o sin la columna obligatoria (422).
    [Fact]
    public async Task Fuera_de_CREADO_de_otra_compania_demasiado_grande_o_sin_columna_no_se_analiza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostico:Carga:TamanoMaximoBytes"] = "2048" }));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var (deA, idA) = await LoteAsync(api, new ConfiguracionEmpresa(), ct, empresas.A);
        var (deB, _) = await LoteAsync(api, new ConfiguracionEmpresa(), ct, empresas.B);
        var (_, enProceso) = await LoteAsync(api, new ConfiguracionEmpresa(), ct, empresas.A, crearConfiguracion: false);
        await using (var db = empresas.A.Db())
        {
            await db.Set<CabeceraDiagnostico>().Where(c => c.Id == enProceso)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, EstadoDiagnostico.EnProceso), ct);
        }

        var ajena = await SubirAsync(deB, idA, Csv("ABC123"), "a.csv", ct);
        var noCreado = await SubirAsync(deA, enProceso, Csv("ABC123"), "a.csv", ct);
        var grande = await SubirAsync(deA, idA, Csv(Placas(400)), "grande.csv", ct);
        var sinColumna = await SubirAsync(deA, idA, Bytes("Placa;Documento", "ABC123;"), "sin-columna.csv", ct);
        using var sinArchivo = new MultipartFormDataContent { { new StringContent("x"), "otro" } };
        var vacio = await deA.PostAsync(RutaCarga(idA), sinArchivo, ct);

        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, await ajena.Content.ReadAsStringAsync(ct));
        (await ajena.Content.ReadAsStringAsync(ct)).Should().NotContain("ABC123");
        noCreado.StatusCode.Should().Be(HttpStatusCode.Conflict, await noCreado.Content.ReadAsStringAsync(ct));
        (await Codigo(noCreado, ct)).Should().Be(ServicioCabecera.CodigoNoEditable);
        grande.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await grande.Content.ReadAsStringAsync(ct));
        (await Codigo(grande, ct)).Should().Be("ARCHIVO_DEMASIADO_GRANDE");
        sinColumna.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await sinColumna.Content.ReadAsStringAsync(ct));
        (await Codigo(sinColumna, ct)).Should().Be("COLUMNA_REQUERIDA");
        vacio.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await vacio.Content.ReadAsStringAsync(ct));
        (await Codigo(vacio, ct)).Should().Be("ARCHIVO_REQUERIDO");
    }

    private static string RutaCarga(Guid id) => $"{Cabeceras.Ruta}/{id}/carga";

    private static byte[] Fixture(string nombre) => File.ReadAllBytes(Path.Combine(Fixtures, nombre));

    private static byte[] Bytes(params string[] lineas) => Encoding.UTF8.GetBytes(string.Join("\n", lineas) + "\n");

    private static byte[] Csv(params string[] identificadores) =>
        Bytes([PlantillaCarga.ColumnaIdentificador, .. identificadores]);

    private static string[] Placas(int cuantas) => [.. Enumerable.Range(0, cuantas).Select(i => $"{(char)('A' + (i / 1000))}BC{i % 1000:D3}")];

    private static JsonElement Fila(JsonElement resumen, int numero) =>
        resumen.GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("fila").GetInt32() == numero);

    private static List<string?> Errores(JsonElement fila) =>
        fila.GetProperty("errores").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();

    private static List<string?> Avisos(JsonElement resumen) =>
        resumen.GetProperty("avisos").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();

    private static async Task<HttpResponseMessage> SubirAsync(HttpClient cliente, Guid id, byte[] bytes, string nombre, CancellationToken ct)
    {
        using var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(bytes);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        contenido.Add(archivo, "archivo", nombre);
        return await cliente.PostAsync(RutaCarga(id), contenido, ct);
    }

    private static async Task<JsonElement> ResumenAsync(HttpClient cliente, Guid id, byte[] csv, CancellationToken ct)
    {
        var respuesta = await SubirAsync(cliente, id, csv, "carga.csv", ct);
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        return JsonDocument.Parse(texto).RootElement;
    }

    /// <summary>Siembra la configuración de la compañía y crea una cabecera SIMPLE en CREADO con RUNT (y el locatario dado).</summary>
    private static async Task<(HttpClient Cliente, Guid Id)> LoteAsync(
        ApiDePrueba api, ConfiguracionEmpresa configuracion, CancellationToken ct, EmpresaDePrueba? empresa = null, string[]? porLocatario = null,
        bool crearConfiguracion = true, string[]? porVehiculo = null, string[]? porPropietario = null)
    {
        empresa ??= DosEmpresas.En(api).A;
        if (crearConfiguracion)
            await SembrarAsync(empresa, configuracion, ct);
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote de carga", "SIMPLE", null, porVehiculo ?? ["RUNT"], porPropietario, porLocatario)), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        return (cliente, (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Doble de fuente externa (HU #13749): todo cliente HTTP del servicio pasa por un manejador que cuenta la llamada y falla.
    /// Solo deja pasar el JWKS de Identidad, que la autenticación necesita.
    /// </summary>
    private sealed class FuenteExternaQueFalla
    {
        private int _llamadas;

        public int Llamadas => Volatile.Read(ref _llamadas);

        public sealed class Manejador(FuenteExternaQueFalla doble) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri?.AbsolutePath.EndsWith("/jwks.json", StringComparison.Ordinal) == true)
                    return base.SendAsync(request, cancellationToken);
                Interlocked.Increment(ref doble._llamadas);
                throw new InvalidOperationException($"El análisis de la carga llamó a una fuente externa: {request.RequestUri}");
            }
        }
    }

    /// <summary>Proveedor de logs que guarda cada mensaje formateado (y su excepción) para buscar PII.</summary>
    private sealed class CapturaDeLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _mensajes = new();

        public IReadOnlyCollection<string> Mensajes => _mensajes;

        public ILogger CreateLogger(string categoryName) => new Registro(_mensajes);

        public void Dispose()
        {
        }

        private sealed class Registro(ConcurrentQueue<string> mensajes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                mensajes.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
