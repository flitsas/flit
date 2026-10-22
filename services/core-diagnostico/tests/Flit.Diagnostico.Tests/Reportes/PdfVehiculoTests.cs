using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Reportes.Marca;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #13848 (F07-H3, F07-C1..C4/C8..C11, R-7, R-20, Q4, Q7) — <c>GET /diagnosticos/{id}/vehiculos/{vid}/pdf</c>: el PDF corporativo
/// de un vehículo con el membrete FLIT. Las pruebas leen el TEXTO del PDF (<see cref="PdfTextExtractor"/>), no solo el código HTTP:
/// encabezado con ID, lote, creador, empresa, fecha y «Diagnóstico Simple»; solo las secciones del alcance; 200 con el vehículo
/// terminado aunque el lote siga EN_PROCESO y 409 <c>VEHICULO_NO_TERMINADO</c> si no; con el lote INCOMPLETO la fuente en ERROR
/// sale «consulta no disponible»; manual y API dan el mismo texto; sin origen, proveedor ni transacción; documentos «****» + 4;
/// misma huella → mismos bytes y un cambio de celda invalida el anterior; 404 para otra compañía o PROPIOS ajeno; 403 sin permiso.
/// Postgres real (base efímera de <see cref="ApiDePrueba"/>); placas TST3## y documentos ficticios cifrados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Reportes.PdfVehiculoTests
/// </code>
/// </example>
public sealed class PdfVehiculoTests
{
    private const string PayloadRunt =
        """{"placa":"TST301","vin":"9ZZPRUEBA00000301","servicio":"PARTICULAR","clase":"AUTOMOVIL","organismo":"STRIA PRUEBA","servicio_que_resolvio":"INTEMPO_SIMULADO","id_transaccion":"TX-SECRETA-301","propietarios":[{"tipo_documento":"C","tipo_propiedad":"PROPIA"}],"locatarios":1}""";

    private const string PayloadSimit =
        """{"semaforo":"AMARILLO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0301","fecha":"2026-09-30","valor":"468500","estado":"PENDIENTE","organismo":"Organismo de prueba"}],"transaccion_proveedor_id":"TX-SIMIT-301"}""";

    private const string PayloadSimitReprocesado =
        """{"semaforo":"ROJO","cantidad_comparendos":2,"comparendos":[{"numero":"TST-C-0302","fecha":"2026-10-01","valor":"1200000","estado":"PENDIENTE","organismo":"Organismo de prueba"},{"numero":"TST-C-0303","fecha":"2026-10-02","valor":"604100","estado":"PENDIENTE","organismo":"Organismo de prueba"}]}""";

    private const string PayloadAvaluo =
        """{"sin_valor_comercial":false,"valor_sugerido":48500000,"fuente_principal":"fasecolda","fuentes":[{"fuente":"fasecolda","estado":"OK","valor":48500000,"muestras":3,"fecha":"2026-10-22"}]}""";

    private const string TituloSimit = "SIMIT — Comparendos del vehículo";

    private static readonly DateTimeOffset Dato = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    // AC1 (F07-C1, F07-C2, Q4) — lote Simple con un vehículo terminado: el texto trae el ID, el nombre del lote, el creador de
    // Identidad, la empresa del claim, la fecha y hora de creación en hora Colombia, «Diagnóstico Simple» y la placa; adjunto
    // diagnostico-{8}-{placa}.pdf sin caché; el membrete y Poppins son copia fiel de la marca de core-api.
    [Fact]
    public async Task Encabezado_trae_id_lote_usuario_empresa_fecha_y_tipo_de_analisis()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new UsuariosIdentidadDePrueba();
        await using var api = await ListadoDePrueba.ArrancarAsync(identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var creador = Guid.NewGuid();
        identidad.Nombres[creador] = "Ana Prueba";
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, creador: creador, nombre: "Flota octubre",
            vehiculos: [new("TST301", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celdas.Single(), PayloadRunt, ct);
        await CreadoEnAsync(empresa, lote.Id, new DateTimeOffset(2026, 10, 22, 19, 30, 0, TimeSpan.Zero), ct);

        var respuesta = await api.Cliente(PdfDePrueba.Token(api, empresa.Id)).GetAsync(PdfDePrueba.Ruta(lote.Id, lote.Vehiculos.Single().Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await CuerpoSiNoEsPdf(respuesta, ct));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        respuesta.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        respuesta.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().Be($"diagnostico-{lote.Id.ToString()[..8]}-TST301.pdf");
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue("lleva documentos enmascarados y datos de la compañía");
        var texto = await PdfDePrueba.TextoAsync(respuesta, ct);
        foreach (var esperado in new[]
                 {
                     lote.Id.ToString(), "Flota octubre", "Ana Prueba", "Empresa de prueba", "2026-10-22 14:30", "Diagnóstico Simple", "TST301",
                     "Página 1 de 1", "Generado el",
                 })
            PdfDePrueba.Tiene(texto, esperado).Should().BeTrue($"el texto del PDF trae «{esperado}»: {texto}");

        // Marca FLIT copiada (no referenciada) de core-api: membrete y Poppins idénticos byte a byte.
        var marca = MarcaCoreApi();
        BrandingAssets.MembreteHeaderSvg.Should().Be(File.ReadAllText(Path.Combine(marca, "Assets", "membrete-hoja-header.svg")));
        BrandingAssets.MembreteFooterSvg.Should().Be(File.ReadAllText(Path.Combine(marca, "Assets", "membrete-hoja-footer.svg")));
        foreach (var fuente in FuentesFlit.Archivos.Append("OFL-LICENSE.txt"))
        {
            using var copia = BrandingAssets.Abrir(fuente);
            using var leida = new MemoryStream();
            await copia.CopyToAsync(leida, ct);
            leida.ToArray().Should().Equal(await File.ReadAllBytesAsync(Path.Combine(marca, "Fonts", fuente), ct), $"{fuente} es copia fiel");
        }
    }

    // AC2 (F07-C3) — alcance sin AVALUO ni fuentes de persona: no hay sección ni título de avalúo ni de personas; RUNT antes que
    // SIMIT (orden de la grilla).
    [Fact]
    public async Task Solo_pinta_las_secciones_del_alcance_sin_espacios_para_las_demas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos:
            [new("TST311", [new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1), new(Fuente.Runt, EstadoCelda.Pendiente, 1),
                new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST311", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST311", Fuente.SimitVehiculo), PayloadSimit, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST311", Fuente.Avaluo), PayloadAvaluo, ct);

        var texto = await PdfDePrueba.TextoOkAsync(api, PdfDePrueba.Token(api, empresa.Id), lote.Id, lote.Vehiculos.Single().Id, ct);

        texto.Should().NotContainAny(["Avalúo", "avalúo", "Avaluo", "AVALUO", "48.500.000", "fasecolda"], "AVALUO no está en el alcance");
        texto.Should().NotContainAny(
            [PdfDePrueba.Plano("RUNT — Propietarios"), PdfDePrueba.Plano("RUNT — Locatarios"), PdfDePrueba.Plano("SIMIT — Comparendos de los")],
            "ninguna fuente de persona en el alcance");
        var runt = texto.IndexOf(PdfDePrueba.Plano("RUNT — Datos del vehículo"), StringComparison.Ordinal);
        var simit = texto.IndexOf(PdfDePrueba.Plano(TituloSimit), StringComparison.Ordinal);
        runt.Should().BeGreaterThanOrEqualTo(0, texto);
        simit.Should().BeGreaterThan(runt, "el orden de las columnas de la grilla");
        PdfDePrueba.Tiene(texto, "TST-C-0301").Should().BeTrue("la sección SIMIT trae su tabla de comparendos");
    }

    // AC3 (F07-C4, R-7) — lote EN_PROCESO: el vehículo 1 terminado (COMPLETADO y N/A) descarga su PDF.
    [Fact]
    public async Task Vehiculo_terminado_descarga_aunque_el_lote_siga_en_proceso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteEnProcesoAsync(empresa, ct);

        var respuesta = await api.Cliente(PdfDePrueba.Token(api, empresa.Id))
            .GetAsync(PdfDePrueba.Ruta(lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST321").Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await CuerpoSiNoEsPdf(respuesta, ct));
        var texto = await PdfDePrueba.TextoAsync(respuesta, ct);
        PdfDePrueba.Tiene(texto, "TST321").Should().BeTrue();
        PdfDePrueba.Tiene(texto, "El vehículo está sin el documento del locatario: no hay a quién consultar.").Should().BeTrue("N/A con su motivo en texto");
        (await CabeceraAsync(empresa, lote.Id, ct)).Estado.Should().Be(EstadoDiagnostico.EnProceso);
    }

    // AC3 (F07-C4, R-7) — lote EN_PROCESO con el vehículo 2 con celdas PENDIENTE: 409 VEHICULO_NO_TERMINADO, sin PDF ni evidencia.
    [Fact]
    public async Task Lote_en_proceso_con_vehiculo_sin_terminar_responde_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteEnProcesoAsync(empresa, ct);
        var sinTerminar = lote.Vehiculos.Single(v => v.Placa == "TST322").Id;

        var respuesta = await api.Cliente(PdfDePrueba.Token(api, empresa.Id)).GetAsync(PdfDePrueba.Ruta(lote.Id, sinTerminar), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        error.GetProperty("code").GetString().Should().Be(EmisionPdf.CodigoVehiculoNoTerminado);
        await using var db = empresa.Db();
        (await db.Set<Evidencia>().CountAsync(e => e.VehiculoId == sinTerminar, ct)).Should().Be(0, "no se genera nada");
    }

    // AC8 (F07-C4, F07-C9, Q7) — lote INCOMPLETO con SIMIT_VEHICULO en ERROR TIMEOUT: 200 y la sección SIMIT dice «consulta no
    // disponible», sin TIMEOUT, causa técnica ni origen.
    [Fact]
    public async Task Lote_incompleto_emite_el_pdf_con_consulta_no_disponible()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Incompleto, alcance: [Fuente.Runt, Fuente.SimitVehiculo],
            vehiculos: [new("TST331", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST331", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST331", Fuente.SimitVehiculo), """{"detalle":"Timeout del proveedor tras 30 s"}""", ct, EstadoCelda.Error, "TIMEOUT");

        var texto = await PdfDePrueba.TextoOkAsync(api, PdfDePrueba.Token(api, empresa.Id), lote.Id, lote.Vehiculos.Single().Id, ct);

        var simit = PdfDePrueba.Seccion(texto, TituloSimit);
        PdfDePrueba.Tiene(simit, SeccionReporte.ConsultaNoDisponible).Should().BeTrue(simit);
        texto.Should().NotContainAny(["TIMEOUT", "Timeout", "timeout", "30 s", "30s", "proveedor"], "sin código de error ni causa técnica");
        texto.Should().NotContain("rigen", "sin origen");
    }

    // AC4 (F07-C9, CA 16, R-20) — dos vehículos con el mismo resultado SIMIT, uno llenado a mano: las secciones SIMIT del texto son
    // idénticas y ningún texto dice manual, operador, proveedor ni transacción.
    [Fact]
    public async Task Celda_manual_y_de_api_producen_el_mismo_texto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos:
        [
            new("TST341", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]),
            new("TST342", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]),
        ]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST341", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST342", Fuente.Runt), PayloadRunt, ct, manual: true);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST341", Fuente.SimitVehiculo), PayloadSimit, ct);
        var manual = await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST342", Fuente.SimitVehiculo), PayloadSimit, ct, manual: true);
        await MismaFechaAsync(empresa, lote.Id, ct);
        var token = PdfDePrueba.Token(api, empresa.Id);

        var deApi = await PdfDePrueba.TextoOkAsync(api, token, lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST341").Id, ct);
        var aMano = await PdfDePrueba.TextoOkAsync(api, token, lote.Id, lote.Vehiculos.Single(v => v.Placa == "TST342").Id, ct);

        PdfDePrueba.Seccion(aMano, TituloSimit).Should().Be(PdfDePrueba.Seccion(deApi, TituloSimit), "la sección SIMIT es idéntica");
        PdfDePrueba.Tiene(deApi, "TST-C-0301").Should().BeTrue();
        foreach (var texto in new[] { deApi, aMano })
        {
            texto.Should().NotContainEquivalentOf("manual");
            texto.Should().NotContainEquivalentOf("operador");
            texto.Should().NotContainEquivalentOf("proveedor");
            texto.Should().NotContainEquivalentOf("transacci");
            texto.Should().NotContain(manual.UsuarioManualId!.Value.ToString());
        }
    }

    // R-20 — todas las fuentes, con celdas manuales y payloads que traen proveedor, transacción y nombres: el texto no tiene origen,
    // usuario manual, proveedor, transacción ni nombres; sí los datos normalizados (prueba de que el PDF no está vacío).
    [Fact]
    public async Task Texto_no_contiene_origen_usuario_manual_proveedor_ni_transaccion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance:
            [Fuente.Runt, Fuente.RuntPropietario, Fuente.RuntLocatario, Fuente.SimitVehiculo, Fuente.SimitPropietario, Fuente.SimitLocatario, Fuente.Avaluo],
            vehiculos:
            [new("TST351", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.RuntPropietario, EstadoCelda.Pendiente, 1),
                new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1),
                new(Fuente.SimitPropietario, EstadoCelda.Pendiente, 1), new(Fuente.SimitLocatario, EstadoCelda.Na, 1), new(Fuente.Avaluo, EstadoCelda.Pendiente, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        var propietario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, "C", "10005678", ct);
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, "C", "20001234", ct, OrigenDocumento.DeclaradoCliente);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST351", Fuente.Runt), PayloadRunt, ct, manual: true);
        var manual = await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST351", Fuente.SimitVehiculo), PayloadSimit, ct, manual: true);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST351", Fuente.Avaluo), PayloadAvaluo, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST351", Fuente.RuntPropietario), Personas(
            (propietario.Id, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","nombre":"NOMBRE FICTICIO","proveedor":"INTEMPO","licencias":[{"categoria":"B1","estado":"VIGENTE","vigente_hasta":"2030-01-01"}]}""")), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST351", Fuente.RuntLocatario), Personas(
            (locatario.Id, "RUNT_PERSONA", "OK", """{"inscrito":false,"estado_conductor":null,"licencias":[]}""")), ct, manual: true);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST351", Fuente.SimitPropietario), Personas(
            (propietario.Id, "SIMIT", "OK", """{"semaforo":"VERDE","cantidad_comparendos":0,"comparendos":[],"id_transaccion":"TX-PERSONA-351"}""")), ct);

        var texto = await PdfDePrueba.TextoOkAsync(api, PdfDePrueba.Token(api, empresa.Id), lote.Id, vehiculo, ct);

        foreach (var titulo in new[] { "RUNT — Datos del vehículo", "RUNT — Propietarios", "RUNT — Locatarios", TituloSimit,
                     "SIMIT — Comparendos de los propietarios", "SIMIT — Comparendos de los locatarios", "Avalúo comercial", "48.500.000", "B1" })
            PdfDePrueba.Tiene(texto, titulo).Should().BeTrue($"el PDF trae «{titulo}»: {texto}");
        texto.Should().NotContainAny(["INTEMPO", "TX-", "servicio_que_resolvio", "NOMBRE FICTICIO", "NOMBREFICTICIO", "PROVEEDOR"]);
        texto.Should().NotContainAny([manual.UsuarioManualId!.Value.ToString(), propietario.Id.ToString(), locatario.Id.ToString()]);
        foreach (var prohibido in new[] { "manual", "operador", "proveedor", "transacci", "origen", "backoffice" })
            texto.Should().NotContainEquivalentOf(prohibido);
    }

    // AC6 (F07-C10) — el propietario con documento terminado en 5678 sale «****5678» y nunca completo; el locatario declarado con
    // su rótulo.
    [Fact]
    public async Task Documento_sale_enmascarado_y_nunca_en_claro()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.RuntPropietario, Fuente.RuntLocatario], vehiculos:
            [new("TST361", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.RuntPropietario, EstadoCelda.Pendiente, 1),
                new(Fuente.RuntLocatario, EstadoCelda.Pendiente, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        var propietario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Propietario, "C", "10005678", ct);
        var locatario = await PersonaAsync(api, empresa, vehiculo, RolPersona.Locatario, "C", "20001234", ct, OrigenDocumento.DeclaradoCliente);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST361", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST361", Fuente.RuntPropietario), Personas(
            (propietario.Id, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}""")), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST361", Fuente.RuntLocatario), Personas(
            (locatario.Id, "RUNT_PERSONA", "OK", """{"inscrito":true,"estado_conductor":"ACTIVO","licencias":[]}""")), ct);

        var texto = await PdfDePrueba.TextoOkAsync(api, PdfDePrueba.Token(api, empresa.Id), lote.Id, vehiculo, ct);

        PdfDePrueba.Tiene(texto, "****5678").Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, "****1234").Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, PersonaReporte.Declarado).Should().BeTrue("el locatario lo declaró el cliente");
        texto.Should().NotContainAny(["10005678", "0005678", "20001234", "0001234"], "el documento nunca sale completo");
    }

    // AC5 (F07-C8) — pedido dos veces sin cambios: los mismos bytes, una sola evidencia y sin regenerar.
    [Fact]
    public async Task Misma_huella_devuelve_los_mismos_bytes_sin_regenerar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteSimitAsync(empresa, "TST371", ct);
        var vehiculo = lote.Vehiculos.Single().Id;
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));

        var primero = await PdfDePrueba.BytesOkAsync(cliente, lote.Id, vehiculo, ct);
        var evidencia = await VigenteAsync(empresa, vehiculo, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(1100), ct);
        var segundo = await PdfDePrueba.BytesOkAsync(cliente, lote.Id, vehiculo, ct);

        segundo.Should().Equal(primero, "misma huella: los bytes guardados, sin regenerar (la fecha de generación no cambia)");
        await using var db = empresa.Db();
        var todas = await db.Set<Evidencia>().AsNoTracking().Where(e => e.VehiculoId == vehiculo).ToListAsync(ct);
        todas.Should().ContainSingle().Which.Id.Should().Be(evidencia.Id);
        todas[0].Tipo.Should().Be(TipoEvidencia.Pdf);
        todas[0].Estado.Should().Be(EstadoEvidencia.Lista);
    }

    // AC5 (F07-C8) — un llenado manual y después un reproceso de una celda del vehículo generan cada uno un PDF nuevo con el dato
    // nuevo e invalidan el anterior (vigente=false, conservado).
    [Fact]
    public async Task Llenado_manual_o_reproceso_invalidan_el_pdf_del_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteSimitAsync(empresa, "TST381", ct);
        var vehiculo = lote.Vehiculos.Single().Id;
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));

        var original = await PdfDePrueba.BytesOkAsync(cliente, lote.Id, vehiculo, ct);
        var primera = await VigenteAsync(empresa, vehiculo, ct);

        await CambiarCeldaAsync(empresa, lote.Celda("TST381", Fuente.SimitVehiculo).Id, PayloadSimitReprocesado, OrigenCelda.Manual, ct);
        var trasManual = await PdfDePrueba.BytesOkAsync(cliente, lote.Id, vehiculo, ct);
        var segunda = await VigenteAsync(empresa, vehiculo, ct);

        await CambiarCeldaAsync(empresa, lote.Celda("TST381", Fuente.SimitVehiculo).Id, PayloadSimit, OrigenCelda.Api, ct);
        var trasReproceso = await PdfDePrueba.BytesOkAsync(cliente, lote.Id, vehiculo, ct);
        var tercera = await VigenteAsync(empresa, vehiculo, ct);

        trasManual.Should().NotEqual(original);
        PdfDePrueba.Tiene(PdfDePrueba.Texto(trasManual), "TST-C-0302").Should().BeTrue("el PDF nuevo trae el dato del llenado manual");
        PdfDePrueba.Tiene(PdfDePrueba.Texto(trasReproceso), "TST-C-0301").Should().BeTrue("el PDF nuevo trae el dato del reproceso");
        new[] { primera.Id, segunda.Id, tercera.Id }.Should().OnlyHaveUniqueItems("cada cambio de celda genera una evidencia nueva");
        new[] { primera.Huella, segunda.Huella, tercera.Huella }.Should().OnlyHaveUniqueItems();
        await using var db = empresa.Db();
        var todas = await db.Set<Evidencia>().AsNoTracking().Where(e => e.VehiculoId == vehiculo).ToListAsync(ct);
        todas.Should().HaveCount(3, "las anteriores se conservan");
        todas.Single(e => e.Vigente).Id.Should().Be(tercera.Id);
    }

    // AC7 (F07-C11) — desde la compañía A: el PDF de un lote de B, un vehículo de B bajo un lote de A y un vehículo de otro lote de
    // A responden 404 NO_ENCONTRADO sin datos de B; con PROPIOS, el lote de otro usuario de A también es 404 (el creador sí descarga).
    [Fact]
    public async Task Otra_compania_y_propios_ajeno_responden_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var deB = await LoteSimitAsync(empresas.B, "TST391", ct, nombre: "Lote secreto de B");
        var creador = PdfDePrueba.Token(api, empresas.A.Id);
        var otro = PdfDePrueba.Token(api, empresas.A.Id);
        var deA = await LoteSimitAsync(empresas.A, "TST392", ct, creador: TableroAyudas.Usuario(creador));
        var otroDeA = await LoteSimitAsync(empresas.A, "TST393", ct, creador: TableroAyudas.Usuario(creador));
        var vehiculoB = deB.Vehiculos.Single().Id;

        foreach (var ruta in new[]
                 {
                     PdfDePrueba.Ruta(deB.Id, vehiculoB), PdfDePrueba.Ruta(deA.Id, vehiculoB), PdfDePrueba.Ruta(deA.Id, otroDeA.Vehiculos.Single().Id),
                 })
        {
            var respuesta = await api.Cliente(creador).GetAsync(ruta, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{ruta}: {cuerpo}");
            JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
            cuerpo.Should().NotContainAny(["Lote secreto de B", "TST391", "TST-C-0301", vehiculoB.ToString(), empresas.B.Id.ToString()]);
        }

        await ConfiguracionAsync(empresas.A, ct);
        var ajena = await api.Cliente(otro).GetAsync(PdfDePrueba.Ruta(deA.Id, deA.Vehiculos.Single().Id), ct);
        var cuerpoAjeno = await ajena.Content.ReadAsStringAsync(ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, "PROPIOS: el lote es de otro usuario");
        cuerpoAjeno.Should().NotContainAny(["TST392", "TST-C-0301"]);
        (await api.Cliente(creador).GetAsync(PdfDePrueba.Ruta(deA.Id, deA.Vehiculos.Single().Id), ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "el creador sí descarga su PDF");
        await using var db = empresas.B.Db();
        (await db.Set<Evidencia>().CountAsync(ct)).Should().Be(0, "nada se generó para B");
    }

    // Permiso (F07-C11) — sin diagnostico.reportes.descargar (aunque vea el lote): 403 sin cuerpo y sin generar.
    [Fact]
    public async Task Sin_permiso_de_descarga_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteSimitAsync(empresa, "TST399", ct);
        var soloVer = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);

        var respuesta = await api.Cliente(soloVer).GetAsync(PdfDePrueba.Ruta(lote.Id, lote.Vehiculos.Single().Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
        await using var db = empresa.Db();
        (await db.Set<Evidencia>().CountAsync(ct)).Should().Be(0);
    }

    /// <summary>Lote EN_PROCESO: TST321 terminado (RUNT COMPLETADO y SIMIT_VEHICULO N/A) y TST322 con SIMIT_VEHICULO PENDIENTE.</summary>
    private static async Task<Tablero.LoteSembrado> LoteEnProcesoAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos:
        [
            new("TST321", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Na, 1)]),
            new("TST322", [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]),
        ]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST321", Fuente.Runt), PayloadRunt, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST322", Fuente.Runt), PayloadRunt, ct);
        return lote;
    }

    /// <summary>Lote con un vehículo terminado: RUNT y SIMIT_VEHICULO COMPLETADO.</summary>
    private static async Task<Tablero.LoteSembrado> LoteSimitAsync(EmpresaDePrueba empresa, string placa, CancellationToken ct, string nombre = "Lote del PDF", Guid? creador = null)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], nombre: nombre, creador: creador,
            vehiculos: [new(placa, [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)])]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Runt), PayloadRunt.Replace("TST301", placa, StringComparison.Ordinal), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.SimitVehiculo), PayloadSimit, ct);
        return lote;
    }

    /// <summary>Reproceso (origen API) o llenado manual de una celda: nuevo payload, nueva fecha y la versión del tablero sube.</summary>
    private static async Task CambiarCeldaAsync(EmpresaDePrueba empresa, Guid celda, string payload, OrigenCelda origen, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var fila = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == celda, ct);
        fila.Payload = payload;
        fila.Origen = origen;
        fila.UsuarioManualId = origen == OrigenCelda.Manual ? Guid.NewGuid() : null;
        fila.DatoObtenidoEn = DateTimeOffset.UtcNow;
        fila.VersionTablero += 1;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Evidencia> VigenteAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Evidencia>().AsNoTracking().SingleAsync(e => e.VehiculoId == vehiculo && e.Vigente, ct);
    }

    private static async Task<CabeceraDiagnostico> CabeceraAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == lote, ct);
    }

    private static async Task ConfiguracionAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios });
        await db.SaveChangesAsync(ct);
    }

    private static async Task MismaFechaAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).ExecuteUpdateAsync(s => s.SetProperty(c => c.DatoObtenidoEn, Dato), ct);
    }

    private static async Task CreadoEnAsync(EmpresaDePrueba empresa, Guid lote, DateTimeOffset creadoEn, CancellationToken ct)
    {
        await using var db = empresa.Db();
        await db.Set<CabeceraDiagnostico>().Where(c => c.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreadoEn, creadoEn), ct);
    }

    private static async Task<PersonaVehiculo> PersonaAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid vehiculo, RolPersona rol, string tipo, string numero, CancellationToken ct,
        OrigenDocumento origen = OrigenDocumento.Proveedor)
    {
        var persona = new PersonaVehiculo
        {
            VehiculoId = vehiculo,
            Rol = rol,
            Orden = 1,
            DocTipo = tipo,
            DocCifrado = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Proteger(numero),
            Origen = origen,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        await using var db = empresa.Db();
        db.Add(persona);
        await db.SaveChangesAsync(ct);
        return persona;
    }

    /// <summary>Payload de una celda de persona con la forma de <c>EjecutorPorPersonas</c> (una persona, orden 1).</summary>
    private static string Personas((Guid Id, string Consulta, string Estado, string Resultado) p) =>
        $$"""{"personas":[{"persona_id":"{{p.Id}}","orden":1,"consulta":"{{p.Consulta}}","estado":"{{p.Estado}}","resultado":{{p.Resultado}},"consultado_en":"2026-10-22T15:00:00+00:00"}]}""";

    private static async Task<string> CuerpoSiNoEsPdf(HttpResponseMessage respuesta, CancellationToken ct) =>
        respuesta.Content.Headers.ContentType?.MediaType == "application/pdf" ? "(pdf)" : await respuesta.Content.ReadAsStringAsync(ct);

    /// <summary>La carpeta de la marca en core-api (origen de la copia), subiendo desde la salida de las pruebas.</summary>
    private static string MarcaCoreApi()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var marca = Path.Combine(dir.FullName, "services", "core-api", "src", "Flit.Infrastructure", "Documents", "Branding");
            if (Directory.Exists(marca))
                return marca;
        }

        throw new InvalidOperationException("No se encontró services/core-api/src/Flit.Infrastructure/Documents/Branding desde la salida de las pruebas.");
    }
}

/// <summary>
/// Ayudas del PDF del vehículo (HU #13848): la ruta, un token con <c>reportes.descargar</c> y la lectura del texto del PDF (aplanado,
/// sin espacios: Skia dibuja glifo a glifo, ver <see cref="PdfTextExtractor.Flatten"/>). También es el lector del cuerpo del escenario
/// de fuga (<see cref="EscenariosDeFuga.RutaPdfVehiculo"/>): un cuerpo que no es PDF (el 404) se devuelve tal cual.
/// </summary>
/// <example>
/// <code>
/// var texto = await PdfDePrueba.TextoOkAsync(api, PdfDePrueba.Token(api, empresa.Id), lote.Id, vehiculo, ct);
/// PdfDePrueba.Tiene(texto, "Diagnóstico Simple").Should().BeTrue();
/// </code>
/// </example>
internal static class PdfDePrueba
{
    private static readonly string[] FinesDeSeccion = ["RUNT—", "SIMIT—", "Avalúocomercial", "Generadoel"];

    public static string Ruta(Guid id, Guid vehiculo) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/vehiculos/{vehiculo}/pdf";

    public static string Token(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.ReportesDescargar], roles: ["usuario_diagnostico"]);

    /// <summary>El texto del PDF aplanado; si la respuesta no es un PDF, el cuerpo tal cual.</summary>
    public static async Task<string> TextoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (respuesta.Content.Headers.ContentType?.MediaType != "application/pdf")
            return await respuesta.Content.ReadAsStringAsync(ct);
        return Texto(await respuesta.Content.ReadAsByteArrayAsync(ct));
    }

    public static string Texto(byte[] pdf) => PdfTextExtractor.Flatten(PdfTextExtractor.Extract(pdf));

    public static string Plano(string literal) => PdfTextExtractor.Flatten(literal);

    /// <summary>¿Está el literal (aplanado) en el texto aplanado, respetando mayúsculas?</summary>
    public static bool Tiene(string texto, string literal) => texto.Contains(Plano(literal), StringComparison.Ordinal);

    public static async Task<byte[]> BytesOkAsync(HttpClient cliente, Guid id, Guid vehiculo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        var respuesta = await cliente.GetAsync(Ruta(id, vehiculo), ct);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        return bytes;
    }

    public static async Task<string> TextoOkAsync(ApiDePrueba api, string token, Guid id, Guid vehiculo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api);
        return Texto(await BytesOkAsync(api.Cliente(token), id, vehiculo, ct));
    }

    /// <summary>
    /// La sección que empieza en <paramref name="titulo"/>, hasta el título de la sección siguiente o el pie (<c>Generado el</c>), lo
    /// que llegue antes.
    /// </summary>
    public static string Seccion(string texto, string titulo)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var inicio = texto.IndexOf(Plano(titulo), StringComparison.Ordinal);
        if (inicio < 0)
            throw new InvalidOperationException($"El PDF no tiene la sección «{titulo}»: {texto}");
        var resto = texto[(inicio + Plano(titulo).Length)..];
        var fin = FinesDeSeccion
            .Select(m => resto.IndexOf(m, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(resto.Length).Min();
        return texto.Substring(inicio, Plano(titulo).Length + fin);
    }
}
