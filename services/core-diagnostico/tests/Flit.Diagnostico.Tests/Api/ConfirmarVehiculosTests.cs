using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13751 (F02-H5), escenarios 2 a 7 — <c>POST /diagnosticos/{id}/vehiculos</c>: el servidor revalida todo lo recibido
/// (no confía en el cliente), descarta inválidos y duplicados, responde 422 sin válidos o por encima de
/// <c>limite_vehiculos_lote</c> (sin persistir nada), cifra el documento del locatario con origen DECLARADO_CLIENTE,
/// guarda el estimado en la cabecera y entrega la lista limpia a <see cref="IEncoladorConsultas"/> en la misma
/// transacción (doble espía). Solo en CREADO; la cabecera de otra compañía responde 404 sin efecto. Postgres real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConfirmarVehiculosTests
/// </code>
/// </example>
public sealed class ConfirmarVehiculosTests
{
    // Escenario 2 (F02-C9, F02-C14) — solo los válidos quedan asociados, en el orden de la captura y sin duplicados.
    [Fact]
    public async Task Solo_los_validos_quedan_asociados_al_diagnostico()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);

        var respuesta = await Lotes.ConfirmarAsync(cliente, id,
        [
            Lotes.Fila("abc-123"), Lotes.Fila("MAL"), Lotes.Fila("9FBTEST1234567890"), Lotes.Fila("ABC123"),
            Lotes.Fila("XYZ98A", "CC", "12AB34"), Lotes.Fila("R12345"),
        ], ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("confirmados").GetInt32().Should().Be(3);
        json.GetProperty("invalidos").GetInt32().Should().Be(2, "MAL y el documento con letras");
        json.GetProperty("duplicados").GetInt32().Should().Be(1, "ABC123 repite a abc-123: gana la primera");
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        vehiculos.Select(v => (v.Orden, v.IdentificadorNorm, v.TipoIdentificador)).Should().Equal(
            (1, "ABC123", TipoIdentificador.PlacaCarro), (2, "9FBTEST1234567890", TipoIdentificador.Vin), (3, "R12345", TipoIdentificador.PlacaRemolque));
        vehiculos[0].IdentificadorOriginal.Should().Be("abc-123");
        vehiculos[0].Placa.Should().Be("ABC123");
        vehiculos[1].Vin.Should().Be("9FBTEST1234567890");
        vehiculos[1].Placa.Should().BeNull();
        vehiculos.Should().OnlyContain(v => v.TenantId == empresa.Id && v.LocatarioDocCifrado == null && v.LocatarioDocOrigen == null);
        json.GetProperty("vehiculos").EnumerateArray().Select(v => v.GetProperty("identificador_norm").GetString())
            .Should().Equal("ABC123", "9FBTEST1234567890", "R12345");
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).Estado.Should().Be(EstadoDiagnostico.Creado, "esta HU no cambia el estado");
    }

    // Escenario 3 (F02-C4) — un inválido enviado directamente a la API no se persiste ni llega al encolador.
    [Fact]
    public async Task Invalido_enviado_a_la_api_no_se_persiste_ni_llega_al_encolador()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);

        // El cliente afirma que son válidos: el servidor no le cree (identificador inválido, recortable, vacío y con espacios).
        var respuesta = await Lotes.ConfirmarAsync(cliente, id,
            [Lotes.Fila("XYZ987"), Lotes.Fila("NO-ES-PLACA"), Lotes.Fila("AB1234567"), Lotes.Fila("9FBTEST12345678901"), Lotes.Fila("   "), Lotes.Fila(null)], ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
        (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Select(v => v.IdentificadorNorm).Should().Equal("XYZ987");
        espia.Entregas.Should().ContainSingle();
        espia.Entregas[0].DiagnosticoId.Should().Be(id);
        espia.Entregas[0].Vehiculos.Select(v => v.IdentificadorNorm).Should().Equal("XYZ987");
        espia.Entregas.SelectMany(e => e.Vehiculos).Select(v => v.IdentificadorNorm)
            .Should().NotContain(["NOESPLACA", "AB1234567", "AB123456", "9FBTEST12345678901", "9FBTEST1234567890", string.Empty]);
    }

    // Escenario 4 (F02-C10) — sin ningún identificador válido (o sin vehículos), 422 SIN_VALIDOS y nada persiste.
    [Fact]
    public async Task Sin_validos_responde_422_y_no_persiste_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);

        var invalidos = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila("MAL"), Lotes.Fila("AB1234567"), Lotes.Fila(null)], ct);
        var vacio = await Lotes.ConfirmarAsync(cliente, id, [], ct);

        // Contrato multipart (decisión «Reenviar el archivo»): un archivo solo con el encabezado es el lote vacío.
        foreach (var respuesta in new[] { invalidos, vacio })
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, texto);
            var json = JsonDocument.Parse(texto).RootElement;
            json.GetProperty("code").GetString().Should().Be("SIN_VALIDOS");
            json.GetProperty("campo").GetString().Should().Be("vehiculos");
            json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        }

        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0);
        espia.Entregas.Should().BeEmpty();
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).EstimadoConsultas.Should().BeNull();
    }

    // Escenario 5 (F02-C11) — limite_vehiculos_lote = 500 y 501 válidos: 422 LIMITE_SUPERADO y no persiste nada.
    [Fact]
    public async Task Limite_por_lote_superado_responde_422_y_no_persiste_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa { LimiteVehiculosLote = 500 }, ct);

        var exceso = await Lotes.ConfirmarAsync(cliente, id, [.. Placas(501).Select(p => Lotes.Fila(p)), Lotes.Fila("MAL")], ct);

        var texto = await exceso.Content.ReadAsStringAsync(ct);
        exceso.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, texto);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("code").GetString().Should().Be("LIMITE_SUPERADO");
        json.GetProperty("campo").GetString().Should().Be("vehiculos");
        json.GetProperty("error").GetString().Should().Contain("501").And.Contain("500");
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0);
        espia.Entregas.Should().BeEmpty();
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).EstimadoConsultas.Should().BeNull();

        // Exactamente en el límite sí se confirma.
        var justo = await Lotes.ConfirmarAsync(cliente, id, Placas(500).Select(p => Lotes.Fila(p)), ct);
        justo.StatusCode.Should().Be(HttpStatusCode.Created, await justo.Content.ReadAsStringAsync(ct));
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(500);
    }

    // Escenario 6 (F02-C13) — el documento del locatario queda en bytea cifrado (no el texto plano) con origen DECLARADO_CLIENTE.
    [Fact]
    public async Task Documento_del_locatario_queda_cifrado_con_origen_DECLARADO_CLIENTE()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);
        const string Documento = "1020304050";

        var respuesta = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila("ABC123", "cc", Documento), Lotes.Fila("XYZ987")], ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        texto.Should().NotContain(Documento, "la respuesta trae el documento enmascarado").And.Contain("****4050");
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        var conDocumento = vehiculos.Single(v => v.IdentificadorNorm == "ABC123");
        conDocumento.LocatarioDocTipo.Should().Be("CC");
        conDocumento.LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente);
        conDocumento.LocatarioDocCifrado.Should().NotBeNullOrEmpty();
        conDocumento.LocatarioDocCifrado.Should().NotEqual(Encoding.UTF8.GetBytes(Documento));
        Encoding.UTF8.GetString(conDocumento.LocatarioDocCifrado!).Should().NotContain(Documento);
        Convert.ToBase64String(conDocumento.LocatarioDocCifrado!).Should().NotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Documento)));
        api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Desproteger(conDocumento.LocatarioDocCifrado!).Should().Be(Documento);
        var sinDocumento = vehiculos.Single(v => v.IdentificadorNorm == "XYZ987");
        sinDocumento.LocatarioDocCifrado.Should().BeNull();
        sinDocumento.LocatarioDocOrigen.Should().BeNull();
        JsonSerializer.Serialize(espia.Entregas).Should().NotContain(Documento, "el encolador no recibe documentos");
    }

    // Escenario 7 (F02-C14) — la lista limpia se entrega al encolador dentro de la transacción y el estimado queda en la cabecera.
    [Fact]
    public async Task Lote_confirmado_se_entrega_al_encolador_en_la_misma_transaccion_y_guarda_el_estimado()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var configuracion = new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.RuntLocatario],
            EstimadoConsultas = EstimadoConsultas.AntesDeLanzar,
        };
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, configuracion, ct, porLocatario: ["RUNT_LOCATARIO"]);

        var respuesta = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila("ABC123", "CC", "1020304050"), Lotes.Fila("XYZ987"), Lotes.Fila("MAL")], ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        espia.Entregas.Should().ContainSingle();
        var entrega = espia.Entregas[0];
        entrega.EnTransaccion.Should().BeTrue("el encolador se invoca dentro de la transacción del guardado");
        entrega.VehiculosVisibles.Should().Be(2, "los vehículos ya están escritos en esa transacción");
        entrega.Vehiculos.Should().BeEquivalentTo(vehiculos.Select(v => new VehiculoAEncolar(v.Id, v.IdentificadorNorm, v.TipoIdentificador)),
            o => o.WithStrictOrdering());
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Creado);
        var estimado = JsonDocument.Parse(cabecera.EstimadoConsultas!).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        estimado.Should().Equal(new Dictionary<string, int> { ["RUNT"] = 2, ["RUNT_LOCATARIO"] = 1 });
        JsonDocument.Parse(texto).RootElement.GetProperty("estimado_consultas").EnumerateArray()
            .ToDictionary(e => e.GetProperty("fuente").GetString()!, e => e.GetProperty("consultas").GetInt32())
            .Should().Equal(estimado, "con ANTES_DE_LANZAR la respuesta trae el estimado");
    }

    // Escenario 7 (F02-C14) — si el encolador falla, la transacción se revierte: ni vehículos ni estimado.
    [Fact]
    public async Task Si_el_encolador_falla_no_queda_nada_persistido()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia { Fallar = true };
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);

        var respuesta = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila("ABC123"), Lotes.Fila("XYZ987")], ct);

        respuesta.IsSuccessStatusCode.Should().BeFalse();
        espia.Entregas.Should().NotBeEmpty("el encolador sí se llamó");
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0);
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).EstimadoConsultas.Should().BeNull();
    }

    // Solo en CREADO (409), una segunda confirmación (409) y la cabecera de otra compañía (404 sin efecto: fuga).
    [Fact]
    public async Task Fuera_de_CREADO_ya_confirmado_o_de_otra_compania_no_se_confirma()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var (deA, idA) = await Lotes.CrearAsync(api, empresas.A, new ConfiguracionEmpresa(), ct);
        var (deB, _) = await Lotes.CrearAsync(api, empresas.B, new ConfiguracionEmpresa(), ct);
        var (_, enProceso) = await Lotes.CrearAsync(api, empresas.A, new ConfiguracionEmpresa(), ct, sembrar: false);
        await using (var db = empresas.A.Db())
        {
            await db.Set<CabeceraDiagnostico>().Where(c => c.Id == enProceso)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, EstadoDiagnostico.EnProceso), ct);
        }

        var ajena = await Lotes.ConfirmarAsync(deB, idA, [Lotes.Fila("ABC123")], ct);
        var noCreado = await Lotes.ConfirmarAsync(deA, enProceso, [Lotes.Fila("ABC123")], ct);
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0, "ni la ajena ni la de EN_PROCESO guardan nada");
        var primera = await Lotes.ConfirmarAsync(deA, idA, [Lotes.Fila("ABC123")], ct);
        var segunda = await Lotes.ConfirmarAsync(deA, idA, [Lotes.Fila("XYZ987")], ct);

        var textoAjena = await ajena.Content.ReadAsStringAsync(ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, textoAjena);
        textoAjena.Should().NotContain(empresas.A.Id.ToString()).And.NotContain("Lote a confirmar");
        noCreado.StatusCode.Should().Be(HttpStatusCode.Conflict, await noCreado.Content.ReadAsStringAsync(ct));
        (await Codigo(noCreado, ct)).Should().Be(ServicioCabecera.CodigoNoEditable);
        primera.StatusCode.Should().Be(HttpStatusCode.Created, await primera.Content.ReadAsStringAsync(ct));
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, await segunda.Content.ReadAsStringAsync(ct));
        (await Codigo(segunda, ct)).Should().Be("VEHICULOS_YA_CONFIRMADOS");
        (await Lotes.VehiculosAsync(api, empresas.A.Id, idA, ct)).Select(v => v.IdentificadorNorm).Should().Equal("ABC123");
        espia.Entregas.Should().ContainSingle().Which.DiagnosticoId.Should().Be(idA);
    }

    private static string[] Placas(int cuantas) => [.. Enumerable.Range(0, cuantas).Select(i => $"{(char)('A' + (i / 1000))}BC{i % 1000:D3}")];
}
