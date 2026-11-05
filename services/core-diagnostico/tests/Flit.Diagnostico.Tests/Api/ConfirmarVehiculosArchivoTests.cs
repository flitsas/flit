using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13751 (F02-H5), complemento «Reenviar el archivo» (decisión del usuario 2026-10-21) — <c>POST /diagnosticos/{id}/vehiculos</c>
/// en multipart: el MISMO archivo del análisis en <c>archivo</c> y las filas corregidas en <c>correcciones</c> (JSON, por
/// número de fila; un campo ausente conserva el del archivo). El servidor re-analiza el archivo, aplica las correcciones y
/// persiste el documento COMPLETO del archivo cifrado con origen DECLARADO_CLIENTE (R-3, escenario 6), aunque el análisis
/// lo devolvió enmascarado. Mismos topes del archivo que la carga (#13749). Postgres real. Fixtures inventados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConfirmarVehiculosArchivoTests
/// </code>
/// </example>
public sealed class ConfirmarVehiculosArchivoTests
{
    private const string Encabezado = "Identificador_Vehiculo;Tipo_Documento_Locatario;Documento_Locatario";
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Api", "Fixtures");

    // Escenario 6 (F02-C13, R-3) — el análisis enmascara el documento, pero reenviando el archivo se guarda completo y cifrado.
    [Fact]
    public async Task Documento_del_archivo_queda_cifrado_con_DECLARADO_CLIENTE_aunque_el_analisis_lo_enmascaro()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);
        const string Documento = "1098765432";
        var archivo = Bytes(Encabezado, $"ABC123;cc;{Documento}", "XYZ987;;");

        using var analisisContenido = new MultipartFormDataContent { { new ByteArrayContent(archivo), "archivo", "lote.csv" } };
        var analisis = await (await cliente.PostAsync($"{Cabeceras.Ruta}/{id}/carga", analisisContenido, ct)).Content.ReadAsStringAsync(ct);
        analisis.Should().NotContain(Documento, "el análisis devuelve el documento enmascarado").And.Contain("****5432");

        var respuesta = await Lotes.ConfirmarArchivoAsync(cliente, id, archivo, "lote.csv", "[]", ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, texto);
        texto.Should().NotContain(Documento).And.Contain("****5432");
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        vehiculos.Select(v => v.IdentificadorNorm).Should().Equal("ABC123", "XYZ987");
        var conDocumento = vehiculos[0];
        conDocumento.LocatarioDocTipo.Should().Be("CC");
        conDocumento.LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente);
        conDocumento.LocatarioDocCifrado.Should().NotBeNullOrEmpty();
        conDocumento.LocatarioDocCifrado.Should().NotEqual(Encoding.UTF8.GetBytes(Documento));
        Encoding.UTF8.GetString(conDocumento.LocatarioDocCifrado!).Should().NotContain(Documento);
        api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Desproteger(conDocumento.LocatarioDocCifrado!).Should().Be(Documento);
        vehiculos[1].LocatarioDocCifrado.Should().BeNull();
        JsonSerializer.Serialize(espia.Entregas).Should().NotContain(Documento);
    }

    // Escenario 6 con el .xlsx de la plantilla — el lector .xlsx alimenta la confirmación igual que el .csv.
    [Fact]
    public async Task El_xlsx_reenviado_guarda_los_documentos_completos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);

        var respuesta = await Lotes.ConfirmarArchivoAsync(cliente, id, File.ReadAllBytes(Path.Combine(Fixtures, "carga-mixta.xlsx")), "carga-mixta.xlsx", null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var xyz = vehiculos.Single(v => v.IdentificadorNorm == "XYZ98A");
        protector.Desproteger(xyz.LocatarioDocCifrado!).Should().Be("1020304050");
        xyz.LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente);
        var vin = vehiculos.Single(v => v.IdentificadorNorm == "9FBTEST1234567890");
        protector.Desproteger(vin.LocatarioDocCifrado!).Should().Be("900111222");
        vehiculos.Should().NotContain(v => v.IdentificadorNorm == "R12345", "su documento lleva letras");
    }

    // Escenario 2/6 — una corrección con documento se aplica; la que solo corrige el identificador conserva el documento del archivo.
    [Fact]
    public async Task Correccion_de_fila_con_documento_se_aplica_y_la_parcial_conserva_el_documento_del_archivo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);
        var archivo = Bytes(Encabezado, "MAL;;", "NO-ES-PLACA;CC;70707070", "XYZ987;;");
        const string Correcciones = """
            [
              { "fila": 2, "identificador": "abc-123", "tipo_documento_locatario": "CC", "documento_locatario": "5566778899" },
              { "fila": 3, "identificador": "DEF456" }
            ]
            """;

        var respuesta = await Lotes.ConfirmarArchivoAsync(cliente, id, archivo, "lote.csv", Correcciones, ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, texto);
        texto.Should().NotContain("5566778899").And.NotContain("70707070");
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        vehiculos.Select(v => (v.Orden, v.IdentificadorNorm)).Should().Equal((1, "ABC123"), (2, "DEF456"), (3, "XYZ987"));
        vehiculos[0].IdentificadorOriginal.Should().Be("abc-123");
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        protector.Desproteger(vehiculos[0].LocatarioDocCifrado!).Should().Be("5566778899");
        vehiculos[0].LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente);
        protector.Desproteger(vehiculos[1].LocatarioDocCifrado!).Should().Be("70707070", "la corrección no trae documento: queda el del archivo");
        vehiculos[1].LocatarioDocTipo.Should().Be("CC");
    }

    // Escenario 3 (F02-C4) — una fila válida del archivo corregida a inválida (o con el documento enmascarado) no se persiste.
    [Fact]
    public async Task Fila_corregida_a_invalida_no_se_persiste()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);
        var archivo = Bytes(Encabezado, "ABC123;;", "XYZ987;CC;1020304050", "DEF456;;");
        const string Correcciones = """
            [
              { "fila": 2, "identificador": "NO-ES-PLACA" },
              { "fila": 3, "documento_locatario": "****4050" }
            ]
            """;

        var respuesta = await Lotes.ConfirmarArchivoAsync(cliente, id, archivo, "lote.csv", Correcciones, ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, texto);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("confirmados").GetInt32().Should().Be(1);
        json.GetProperty("invalidos").GetInt32().Should().Be(2);
        (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Select(v => v.IdentificadorNorm).Should().Equal("DEF456");
        espia.Entregas.SelectMany(e => e.Vehiculos).Select(v => v.IdentificadorNorm).Should().Equal("DEF456");
    }

    // Mismas reglas del archivo que la carga (#13749): .xls 422, pdf 415, tope 413, sin archivo 422; correcciones mal formadas 422.
    [Fact]
    public async Task Archivo_no_admitido_demasiado_grande_ausente_o_correcciones_invalidas_no_confirman()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostico:Carga:TamanoMaximoBytes"] = "2048" }));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);
        var valido = Bytes(Encabezado, "ABC123;;");

        var xls = await Lotes.ConfirmarArchivoAsync(cliente, id, File.ReadAllBytes(Path.Combine(Fixtures, "carga.xls")), "carga.xls", null, ct);
        var pdf = await Lotes.ConfirmarArchivoAsync(cliente, id, File.ReadAllBytes(Path.Combine(Fixtures, "carga.pdf")), "carga.pdf", null, ct);
        var xlsxRenombrado = await Lotes.ConfirmarArchivoAsync(cliente, id, valido, "lote.xlsx", null, ct);
        var grande = await Lotes.ConfirmarArchivoAsync(
            cliente, id, Bytes([Encabezado, .. Enumerable.Range(0, 400).Select(i => $"A{i:D2}BC{i % 1000:D3};;")]), "grande.csv", null, ct);
        using var sinArchivo = new MultipartFormDataContent { { new StringContent("[]"), "correcciones" } };
        var ausente = await cliente.PostAsync(Lotes.RutaVehiculos(id), sinArchivo, ct);
        var json = await cliente.PostAsync(
            Lotes.RutaVehiculos(id), new StringContent("""{"vehiculos":[{"identificador":"ABC123"}]}""", Encoding.UTF8, "application/json"), ct);
        var correccionMal = await Lotes.ConfirmarArchivoAsync(cliente, id, valido, "lote.csv", "{no es json", ct);
        var filaInexistente = await Lotes.ConfirmarArchivoAsync(cliente, id, valido, "lote.csv", """[{"fila": 99, "identificador": "XYZ987"}]""", ct);
        var sinNumero = await Lotes.ConfirmarArchivoAsync(cliente, id, valido, "lote.csv", """[{"identificador": "XYZ987"}]""", ct);

        xls.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await xls.Content.ReadAsStringAsync(ct));
        (await Codigo(xls, ct)).Should().Be("FORMATO_XLS_NO_ADMITIDO");
        pdf.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType, await pdf.Content.ReadAsStringAsync(ct));
        (await Codigo(pdf, ct)).Should().Be("FORMATO_NO_ADMITIDO");
        xlsxRenombrado.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType, await xlsxRenombrado.Content.ReadAsStringAsync(ct));
        (await Codigo(xlsxRenombrado, ct)).Should().Be("ARCHIVO_ILEGIBLE");
        grande.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await grande.Content.ReadAsStringAsync(ct));
        (await Codigo(grande, ct)).Should().Be("ARCHIVO_DEMASIADO_GRANDE");
        foreach (var sinArchivoValido in new[] { ausente, json })
        {
            sinArchivoValido.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await sinArchivoValido.Content.ReadAsStringAsync(ct));
            (await Codigo(sinArchivoValido, ct)).Should().Be("ARCHIVO_REQUERIDO");
        }

        foreach (var mala in new[] { correccionMal, filaInexistente, sinNumero })
        {
            var texto = await mala.Content.ReadAsStringAsync(ct);
            mala.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, texto);
            var error = JsonDocument.Parse(texto).RootElement;
            error.GetProperty("code").GetString().Should().Be("CORRECCIONES_INVALIDAS");
            error.GetProperty("campo").GetString().Should().Be("correcciones");
        }

        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0, "ningún rechazo guarda vehículos");
        var despues = await Lotes.ConfirmarArchivoAsync(cliente, id, valido, "lote.csv", null, ct);
        despues.StatusCode.Should().Be(HttpStatusCode.Accepted, await despues.Content.ReadAsStringAsync(ct));
    }

    // Fuga entre compañías — B reenvía un archivo contra la cabecera de A: 404, nada persiste y la respuesta no lleva nada de A ni del archivo.
    [Fact]
    public async Task Archivo_contra_la_cabecera_de_otra_compania_responde_404_sin_efecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var (_, idA) = await Lotes.CrearAsync(api, empresas.A, new ConfiguracionEmpresa(), ct);
        var (deB, idB) = await Lotes.CrearAsync(api, empresas.B, new ConfiguracionEmpresa(), ct);
        var archivo = Bytes(Encabezado, "ABC123;CC;31313131");

        var ajena = await Lotes.ConfirmarArchivoAsync(deB, idA, archivo, "lote.csv", """[{"fila": 2, "documento_locatario": "42424242"}]""", ct);

        var texto = await ajena.Content.ReadAsStringAsync(ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, texto);
        texto.Should().NotContain(empresas.A.Id.ToString()).And.NotContain("31313131").And.NotContain("42424242").And.NotContain("Lote a confirmar");
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0);
        espia.Entregas.Should().BeEmpty();
        (await Lotes.VehiculosAsync(api, empresas.B.Id, idB, ct)).Should().BeEmpty("tampoco escribe en la cabecera propia de B");
    }

    // Escenario 6 (F02-C13) — ni el documento del archivo ni el de la corrección aparecen en los logs de la petición.
    [Fact]
    public async Task Los_documentos_del_archivo_y_de_las_correcciones_no_aparecen_en_logs()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await Lotes.CrearAsync(api, DosEmpresas.En(api).A, new ConfiguracionEmpresa(), ct);
        var archivo = Bytes(Encabezado, "ABC123;CC;61616161", "XYZ987;CC;62626262", "MAL;CC;63636363");

        var respuesta = await Lotes.ConfirmarArchivoAsync(cliente, id, archivo, "lote.csv", """[{"fila": 3, "documento_locatario": "64646464"}]""", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        logs.Mensajes.Should().Contain(m => m.Contains(id.ToString(), StringComparison.Ordinal), "la confirmación deja su log de conteos");
        logs.Mensajes.Should().NotContain(m =>
            m.Contains("61616161", StringComparison.Ordinal) || m.Contains("62626262", StringComparison.Ordinal)
            || m.Contains("63636363", StringComparison.Ordinal) || m.Contains("64646464", StringComparison.Ordinal));
    }

    private static byte[] Bytes(params string[] lineas) => Encoding.UTF8.GetBytes(string.Join("\n", lineas) + "\n");
}
