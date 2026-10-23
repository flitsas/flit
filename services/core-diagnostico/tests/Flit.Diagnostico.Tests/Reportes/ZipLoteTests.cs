using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #13850 (F07-H5, F07-C4/C7/C8/C9/C11/C12, R-7, R-20, Q2, Q5, Q7) — ZIP de los PDF del lote en segundo plano:
/// <c>POST /diagnosticos/{id}/evidencias {"tipo":"ZIP"}</c> responde 202 (o 200 si ya hay uno LISTA con la misma huella), el
/// empaquetador de la cola lo genera (aquí se llama directo: el trabajador hospedado está apagado en las pruebas) y
/// <c>GET …/evidencias/{eid}</c> / <c>GET …/evidencias/{eid}/archivo</c> dan el estado y el archivo. Las pruebas leen el CONTENIDO
/// del ZIP: los nombres de sus entradas, <c>indice.txt</c> y el texto de cada PDF (<see cref="PdfTextExtractor"/>). Postgres real
/// (base efímera de <see cref="ApiDePrueba"/>); placas TST5## y payloads ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Reportes.ZipLoteTests
/// </code>
/// </example>
public sealed class ZipLoteTests
{
    private const string PayloadRunt =
        """{"placa":"TST501","vin":"9ZZPRUEBA00000501","servicio":"PARTICULAR","clase":"AUTOMOVIL","organismo":"STRIA PRUEBA","servicio_que_resolvio":"INTEMPO_SIMULADO","id_transaccion":"TX-SECRETA-501","propietarios":[{"tipo_documento":"C","tipo_propiedad":"PROPIA"}],"locatarios":0}""";

    private const string PayloadSimit =
        """{"semaforo":"AMARILLO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0501","fecha":"2026-09-30","valor":"468500","estado":"PENDIENTE","organismo":"Organismo de prueba"}],"transaccion_proveedor_id":"TX-SIMIT-501"}""";

    private const string PayloadSimitManual =
        """{"semaforo":"ROJO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0502","fecha":"2026-10-01","valor":"1200000","estado":"PENDIENTE","organismo":"Organismo de prueba"}]}""";

    private const string TituloSimit = "SIMIT — Comparendos del vehículo";

    private static readonly DateTimeOffset Dato = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    // AC1 (F07-C7, F07-C12) — el pedido responde 202 con el ZIP en PENDIENTE; el empaquetador lo deja LISTA con su fecha, la
    // descarga es application/zip «diagnostico-{8}.zip» sin caché con un PDF por vehículo y el índice, la evidencia guarda quién
    // la pidió y la empresa del claim, y el temporal se borra.
    [Fact]
    public async Task Pedido_responde_202_y_el_trabajador_lo_deja_lista()
    {
        var ct = TestContext.Current.CancellationToken;
        var temporal = Directory.CreateTempSubdirectory("dgn-zip-").FullName;
        try
        {
            await using var api = await ZipDePrueba.ArrancarAsync(b => b.Configuration["Diagnostico:Evidencias:DirectorioTemporal"] = temporal);
            Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
            var empresa = DosEmpresas.En(api).A;
            var lote = await LoteTerminadoAsync(empresa, "TST501", ct, "TST502");
            var token = PdfDePrueba.Token(api, empresa.Id);
            var cliente = api.Cliente(token);

            var (respuesta, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);

            respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, pedido.ToString());
            var eid = pedido.GetProperty("id").GetGuid();
            respuesta.Headers.Location!.ToString().Should().EndWith(ZipDePrueba.RutaEstado(lote.Id, eid));
            pedido.GetProperty("tipo").GetString().Should().Be("ZIP");
            pedido.GetProperty("estado").GetString().Should().Be("PENDIENTE");
            pedido.GetProperty("vehiculos_incluidos").GetInt32().Should().Be(2);
            pedido.GetProperty("vehiculos_pendientes").GetInt32().Should().Be(0);
            (await ZipDePrueba.EstadoAsync(cliente, lote.Id, eid, ct)).GetProperty("estado").GetString().Should().Be("PENDIENTE");

            (await ZipDePrueba.DrenarAsync(api, ct)).Should().Be(1, "el empaquetador toma el ZIP una sola vez");

            var estado = await ZipDePrueba.EstadoAsync(cliente, lote.Id, eid, ct);
            estado.GetProperty("estado").GetString().Should().Be("LISTA");
            estado.GetProperty("generado_en").ValueKind.Should().Be(JsonValueKind.String);
            estado.GetProperty("creado_en").ValueKind.Should().Be(JsonValueKind.String);
            estado.GetProperty("tipo_error").ValueKind.Should().Be(JsonValueKind.Null);
            var descarga = await cliente.GetAsync(ZipDePrueba.RutaArchivo(lote.Id, eid), ct);
            descarga.StatusCode.Should().Be(HttpStatusCode.OK, await descarga.Content.ReadAsStringAsync(ct));
            descarga.Content.Headers.ContentType!.MediaType.Should().Be(ZipDePrueba.ContentType);
            descarga.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
            descarga.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().Be($"diagnostico-{lote.Id.ToString()[..8]}.zip");
            descarga.Headers.CacheControl!.NoStore.Should().BeTrue("lleva documentos enmascarados y datos de la compañía");
            var zip = ZipDePrueba.Leer(await descarga.Content.ReadAsByteArrayAsync(ct));
            zip.Entradas.Keys.Should().BeEquivalentTo(
                [$"diagnostico-{lote.Id.ToString()[..8]}-TST501.pdf", $"diagnostico-{lote.Id.ToString()[..8]}-TST502.pdf", "indice.txt"]);
            PdfDePrueba.Tiene(zip.TextoPdf("TST501"), "TST501").Should().BeTrue();
            PdfDePrueba.Tiene(zip.TextoPdf("TST502"), "TST502").Should().BeTrue();

            await using var db = empresa.Db();
            var evidencia = await db.Set<Evidencia>().AsNoTracking().SingleAsync(e => e.Id == eid, ct);
            evidencia.Tipo.Should().Be(TipoEvidencia.Zip);
            evidencia.Estado.Should().Be(EstadoEvidencia.Lista);
            evidencia.Vigente.Should().BeTrue();
            evidencia.SolicitadoPor.Should().Be(TableroAyudas.Usuario(token), "la evidencia guarda quién la pidió");
            evidencia.EmpresaNombre.Should().Be("Empresa de prueba", "claim company_name del que la pidió");
            evidencia.Intentos.Should().Be(1);
            evidencia.LeaseHasta.Should().BeNull();
            evidencia.TamanoBytes.Should().BeGreaterThan(0);
            Directory.EnumerateFileSystemEntries(temporal).Should().BeEmpty("el ZIP temporal se borra siempre");
        }
        finally
        {
            Directory.Delete(temporal, recursive: true);
        }
    }

    // AC1 (F07-C7) — lote EN_PROCESO de cuatro vehículos con tres terminados: el ZIP trae tres PDF y el índice lista los cuatro,
    // el cuarto como fuera del ZIP con su estado, sin documentos.
    [Fact]
    public async Task Lote_en_proceso_incluye_solo_los_terminados_y_un_indice_de_los_que_faltan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos:
        [
            Vehiculo("TST511"), Vehiculo("TST512"), Vehiculo("TST513"), Vehiculo("TST514"),
        ]);
        foreach (var placa in new[] { "TST511", "TST512", "TST513" })
            await CompletarAsync(empresa, lote, placa, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST514", Fuente.Runt), Runt("TST514"), ct);
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));

        var (respuesta, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, pedido.ToString());
        pedido.GetProperty("vehiculos_incluidos").GetInt32().Should().Be(3);
        pedido.GetProperty("vehiculos_pendientes").GetInt32().Should().Be(1);
        await ZipDePrueba.DrenarAsync(api, ct);

        var zip = await ZipDePrueba.ArchivoOkAsync(cliente, lote.Id, pedido.GetProperty("id").GetGuid(), ct);

        zip.Pdfs.Should().HaveCount(3);
        zip.Pdfs.Should().OnlyContain(n => !n.Contains("TST514", StringComparison.Ordinal), "el cuarto no ha terminado");
        foreach (var placa in new[] { "TST511", "TST512", "TST513" })
            PdfDePrueba.Tiene(zip.TextoPdf(placa), placa).Should().BeTrue();
        var lineas = zip.Indice.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lineas.Should().Contain(l => l.Contains("TST514", StringComparison.Ordinal) && l.Contains("En proceso", StringComparison.Ordinal)
            && l.Contains("No incluido", StringComparison.Ordinal), zip.Indice);
        foreach (var placa in new[] { "TST511", "TST512", "TST513" })
            lineas.Should().Contain(l => l.Contains(placa, StringComparison.Ordinal) && l.Contains("Terminado", StringComparison.Ordinal)
                && l.Contains("Incluido", StringComparison.Ordinal) && !l.Contains("No incluido", StringComparison.Ordinal), zip.Indice);
        zip.Indice.Should().Contain(lote.Id.ToString());
        zip.Indice.Should().NotContainAny(["****", "99999123"], "el índice no lleva documentos");
    }

    // AC7 (F07-C4, F07-C7, Q7) — lote INCOMPLETO con dos terminados y uno con SIMIT en ERROR: el ZIP trae los tres PDF, el del
    // vehículo con error dice «consulta no disponible» en SIMIT (sin código ni causa) y el índice no deja ninguno fuera.
    [Fact]
    public async Task Lote_incompleto_incluye_los_vehiculos_con_error_con_consulta_no_disponible()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Incompleto, alcance: [Fuente.Runt, Fuente.SimitVehiculo],
            vehiculos: [Vehiculo("TST521"), Vehiculo("TST522"), Vehiculo("TST523")]);
        await CompletarAsync(empresa, lote, "TST521", ct);
        await CompletarAsync(empresa, lote, "TST522", ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST523", Fuente.Runt), Runt("TST523"), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST523", Fuente.SimitVehiculo), """{"detalle":"Timeout del proveedor tras 30 s"}""", ct, EstadoCelda.Error, "TIMEOUT");
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));

        var (respuesta, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, pedido.ToString());
        pedido.GetProperty("vehiculos_incluidos").GetInt32().Should().Be(3);
        pedido.GetProperty("vehiculos_pendientes").GetInt32().Should().Be(0);
        await ZipDePrueba.DrenarAsync(api, ct);
        var zip = await ZipDePrueba.ArchivoOkAsync(cliente, lote.Id, pedido.GetProperty("id").GetGuid(), ct);

        zip.Pdfs.Should().HaveCount(3);
        var conError = zip.TextoPdf("TST523");
        PdfDePrueba.Tiene(PdfDePrueba.Seccion(conError, TituloSimit), SeccionReporte.ConsultaNoDisponible).Should().BeTrue(conError);
        conError.Should().NotContainAny(["TIMEOUT", "Timeout", "timeout", "30s", "proveedor"], "sin código de error ni causa técnica");
        PdfDePrueba.Tiene(PdfDePrueba.Seccion(zip.TextoPdf("TST521"), TituloSimit), "TST-C-0501").Should().BeTrue();
        zip.Indice.Should().NotContain("No incluido", "con el lote INCOMPLETO ninguno queda fuera");
        zip.Indice.Split('\n').Should().Contain(l => l.Contains("TST523", StringComparison.Ordinal) && l.Contains("Con error", StringComparison.Ordinal), zip.Indice);
        zip.Indice.Should().NotContainAny(["TIMEOUT", "Timeout"]);
    }

    // AC2 (F07-C8) — un ZIP LISTA y vigente: pedirlo otra vez sin cambios responde 200 con el mismo id y no encola nada.
    [Fact]
    public async Task Pedido_sin_cambios_devuelve_el_mismo_zip()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteTerminadoAsync(empresa, "TST531", ct);
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));
        var (_, primero) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
        await ZipDePrueba.DrenarAsync(api, ct);

        var (respuesta, segundo) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, segundo.ToString());
        segundo.GetProperty("id").GetGuid().Should().Be(primero.GetProperty("id").GetGuid());
        segundo.GetProperty("estado").GetString().Should().Be("LISTA");
        (await ZipDePrueba.DrenarAsync(api, ct)).Should().Be(0, "no se encola nada");
        (await ZipsAsync(empresa, lote.Id, ct)).Should().ContainSingle();
    }

    // AC2 (F07-C8) — pedidos repetidos (también simultáneos) mientras el ZIP está en la cola: el mismo id, una sola fila.
    [Fact]
    public async Task Pedido_repetido_en_curso_no_duplica()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteTerminadoAsync(empresa, "TST532", ct);
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));

        var simultaneos = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ZipDePrueba.PedirAsync(cliente, lote.Id, ct)));
        var (_, despues) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);

        var id = despues.GetProperty("id").GetGuid();
        simultaneos.Should().OnlyContain(p => p.Respuesta.StatusCode == HttpStatusCode.Accepted && p.Cuerpo.GetProperty("id").GetGuid() == id,
            "seis pedidos a la vez y uno después reciben el mismo ZIP en la cola");
        despues.GetProperty("estado").GetString().Should().Be("PENDIENTE");
        (await ZipsAsync(empresa, lote.Id, ct)).Should().ContainSingle().Which.Estado.Should().Be(EstadoEvidencia.Pendiente);
        (await ZipDePrueba.DrenarAsync(api, ct)).Should().Be(1);
    }

    // AC3 (F07-C8) — ZIP LISTA y vigente; el backoffice llena a mano una celda de un vehículo incluido: el pedido encola uno
    // nuevo (202, otro id) mientras el anterior sigue vigente y descargable; al quedar LISTA el nuevo, el anterior pasa a no vigente.
    [Fact]
    public async Task Llenado_manual_cambia_la_huella_del_zip()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteTerminadoAsync(empresa, "TST533", ct);
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));
        var (_, primero) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
        await ZipDePrueba.DrenarAsync(api, ct);
        var anterior = primero.GetProperty("id").GetGuid();

        await LlenarAManoAsync(empresa, lote.Celda("TST533", Fuente.SimitVehiculo).Id, PayloadSimitManual, ct);
        var (respuesta, segundo) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, segundo.ToString());
        var nuevo = segundo.GetProperty("id").GetGuid();
        nuevo.Should().NotBe(anterior);
        (await ZipsAsync(empresa, lote.Id, ct)).Single(e => e.Id == anterior).Vigente.Should().BeTrue("mientras el nuevo no está LISTA");
        (await ZipDePrueba.ArchivoOkAsync(cliente, lote.Id, anterior, ct)).Pdfs.Should().ContainSingle();

        await ZipDePrueba.DrenarAsync(api, ct);

        var zips = await ZipsAsync(empresa, lote.Id, ct);
        zips.Should().HaveCount(2, "el anterior se conserva");
        zips.Single(e => e.Id == anterior).Vigente.Should().BeFalse();
        zips.Single(e => e.Id == nuevo).Should().Match<Evidencia>(e => e.Vigente && e.Estado == EstadoEvidencia.Lista);
        zips[0].Huella.Should().NotBe(zips[1].Huella);
        var zip = await ZipDePrueba.ArchivoOkAsync(cliente, lote.Id, nuevo, ct);
        PdfDePrueba.Tiene(zip.TextoPdf("TST533"), "TST-C-0502").Should().BeTrue("el PDF nuevo trae el dato del llenado manual");
    }

    // AC5 (F07-C7) — lote sin vehículos terminados: 409 SIN_VEHICULOS_TERMINADOS y no se crea evidencia.
    [Fact]
    public async Task Sin_vehiculos_terminados_responde_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos: [Vehiculo("TST541"), Vehiculo("TST542")]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST541", Fuente.Runt), Runt("TST541"), ct);

        var (respuesta, cuerpo) = await ZipDePrueba.PedirAsync(api.Cliente(PdfDePrueba.Token(api, empresa.Id)), lote.Id, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, cuerpo.ToString());
        cuerpo.GetProperty("code").GetString().Should().Be("SIN_VEHICULOS_TERMINADOS");
        (await ZipsAsync(empresa, lote.Id, ct)).Should().BeEmpty("no se crea evidencia");
    }

    // Contrato (F07-C7) — otro tipo de evidencia o sin tipo: 422 TIPO_INVALIDO con el campo, sin crear nada.
    [Fact]
    public async Task Otro_tipo_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteTerminadoAsync(empresa, "TST543", ct);
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));

        foreach (var tipo in new[] { "PDF", "XLSX", "zip", string.Empty })
        {
            var (respuesta, cuerpo) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct, tipo);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, $"{tipo}: {cuerpo}");
            cuerpo.GetProperty("code").GetString().Should().Be("TIPO_INVALIDO");
            cuerpo.GetProperty("campo").GetString().Should().Be("tipo");
        }

        (await ZipsAsync(empresa, lote.Id, ct)).Should().BeEmpty();
    }

    // AC1 (F07-C12) — el archivo de un ZIP que no está LISTA: 409 EVIDENCIA_NO_LISTA.
    [Fact]
    public async Task Archivo_antes_de_lista_responde_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteTerminadoAsync(empresa, "TST544", ct);
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));
        var (_, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);

        var respuesta = await cliente.GetAsync(ZipDePrueba.RutaArchivo(lote.Id, pedido.GetProperty("id").GetGuid()), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("EVIDENCIA_NO_LISTA");
    }

    // F07-C12 — el almacén falla al subir el ZIP: reintenta con backoff (reloj inyectado, sin esperas reales) hasta
    // MaxReintentos=2 y queda en ERROR con tipo_error; antes del backoff no se retoma, y el temporal se borra en cada intento.
    [Fact]
    public async Task Fallo_del_almacen_reintenta_y_luego_queda_en_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var fabrica = new FabricaAlmacenQueFallaEnZip();
        var temporal = Directory.CreateTempSubdirectory("dgn-zip-").FullName;
        try
        {
            await using var api = await ZipDePrueba.ArrancarAsync(b =>
            {
                b.Services.AddSingleton<TimeProvider>(reloj);
                b.Services.AddSingleton<IFabricaAlmacen>(fabrica);
                b.Configuration["Diagnostico:Evidencias:DirectorioTemporal"] = temporal;
            });
            Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
            var empresa = DosEmpresas.En(api).A;
            var lote = await LoteTerminadoAsync(empresa, "TST545", ct);
            var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));
            var (_, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
            var eid = pedido.GetProperty("id").GetGuid();

            (await ZipDePrueba.ProcesarAsync(api, ct)).Should().BeTrue();
            (await EvidenciaAsync(empresa, eid, ct)).Should().Match<Evidencia>(e => e.Estado == EstadoEvidencia.Pendiente && e.Intentos == 1 && e.TipoError == null);
            (await ZipDePrueba.EstadoAsync(cliente, lote.Id, eid, ct)).GetProperty("estado").GetString().Should().Be("PENDIENTE");
            (await ZipDePrueba.ProcesarAsync(api, ct)).Should().BeFalse("el backoff de 30 s no ha pasado");

            reloj.Avanzar(TimeSpan.FromSeconds(31));
            (await ZipDePrueba.ProcesarAsync(api, ct)).Should().BeTrue();
            (await EvidenciaAsync(empresa, eid, ct)).Should().Match<Evidencia>(e => e.Estado == EstadoEvidencia.Pendiente && e.Intentos == 2);
            reloj.Avanzar(TimeSpan.FromSeconds(31));
            (await ZipDePrueba.ProcesarAsync(api, ct)).Should().BeFalse("el segundo backoff es de 60 s");

            reloj.Avanzar(TimeSpan.FromSeconds(30));
            (await ZipDePrueba.ProcesarAsync(api, ct)).Should().BeTrue();

            var final = await EvidenciaAsync(empresa, eid, ct);
            final.Estado.Should().Be(EstadoEvidencia.Error);
            final.Intentos.Should().Be(3, "un intento más MaxReintentos=2");
            final.TipoError.Should().Be("ALMACEN_NO_DISPONIBLE");
            final.LeaseHasta.Should().BeNull();
            fabrica.IntentosZip.Should().Be(3);
            (await ZipDePrueba.ProcesarAsync(api, ct)).Should().BeFalse("ERROR es definitivo");
            var estado = await ZipDePrueba.EstadoAsync(cliente, lote.Id, eid, ct);
            estado.GetProperty("estado").GetString().Should().Be("ERROR");
            estado.GetProperty("tipo_error").GetString().Should().Be("ALMACEN_NO_DISPONIBLE");
            (await cliente.GetAsync(ZipDePrueba.RutaArchivo(lote.Id, eid), ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            Directory.EnumerateFileSystemEntries(temporal).Should().BeEmpty("el temporal se borra también cuando falla");

            var (otra, nuevo) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
            otra.StatusCode.Should().Be(HttpStatusCode.Accepted, "un ZIP en ERROR no impide pedir otro");
            nuevo.GetProperty("id").GetGuid().Should().NotBe(eid);
        }
        finally
        {
            Directory.Delete(temporal, recursive: true);
        }
    }

    // AC4 (F07-C9, R-20) — un vehículo con celdas llenadas a mano y otro con las mismas de API: el texto de cada PDF del ZIP (y el
    // índice) no tiene origen, usuario manual, proveedor ni transacción, y la sección SIMIT de los dos es idéntica.
    [Fact]
    public async Task Ningun_pdf_del_zip_contiene_origen_manual()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], vehiculos: [Vehiculo("TST551"), Vehiculo("TST552")]);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST551", Fuente.Runt), Runt("TST551"), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST551", Fuente.SimitVehiculo), PayloadSimit, ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST552", Fuente.Runt), Runt("TST552"), ct, manual: true);
        var manual = await TableroAyudas.PayloadAsync(empresa, lote.Celda("TST552", Fuente.SimitVehiculo), PayloadSimit, ct, manual: true);
        await MismaFechaAsync(empresa, lote.Id, ct);
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));
        var (_, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
        await ZipDePrueba.DrenarAsync(api, ct);

        var zip = await ZipDePrueba.ArchivoOkAsync(cliente, lote.Id, pedido.GetProperty("id").GetGuid(), ct);

        zip.Pdfs.Should().HaveCount(2);
        PdfDePrueba.Seccion(zip.TextoPdf("TST552"), TituloSimit).Should().Be(PdfDePrueba.Seccion(zip.TextoPdf("TST551"), TituloSimit), "manual = API");
        foreach (var texto in zip.Pdfs.Select(zip.TextoPdf).Append(zip.Indice))
        {
            texto.Should().NotContainAny(["INTEMPO", "TX-", "servicio_que_resolvio", manual.UsuarioManualId!.Value.ToString()]);
            foreach (var prohibido in new[] { "manual", "operador", "proveedor", "transacci", "origen", "backoffice" })
                texto.Should().NotContainEquivalentOf(prohibido);
        }
    }

    // AC6 (F07-C11) — un ZIP de la compañía B: con el token de A, pedir el ZIP del lote de B, consultar su estado o bajar su
    // archivo (también bajo un lote de A) responde 404 sin datos de B; el ZIP de A, generado en la misma cola, no tiene nada de B;
    // con PROPIOS, otro usuario de A tampoco ve el ZIP de un lote ajeno.
    [Fact]
    public async Task Otra_compania_responde_404_en_pedido_estado_y_archivo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var deB = await LoteTerminadoAsync(empresas.B, "TST561", ct, nombre: "Lote secreto de B");
        var creadorA = PdfDePrueba.Token(api, empresas.A.Id);
        var deA = await LoteTerminadoAsync(empresas.A, "TST562", ct, creador: TableroAyudas.Usuario(creadorA));
        var (_, pedidoB) = await ZipDePrueba.PedirAsync(api.Cliente(PdfDePrueba.Token(api, empresas.B.Id)), deB.Id, ct);
        var (_, pedidoA) = await ZipDePrueba.PedirAsync(api.Cliente(creadorA), deA.Id, ct);
        (await ZipDePrueba.DrenarAsync(api, ct)).Should().Be(2, "la cola es de todas las compañías");
        var eidB = pedidoB.GetProperty("id").GetGuid();
        var eidA = pedidoA.GetProperty("id").GetGuid();
        var deBNoCruza = new[] { "Lote secreto de B", "TST561", deB.Id.ToString(), empresas.B.Id.ToString() };
        var cliente = api.Cliente(creadorA);

        var (pedido, cuerpoPedido) = await ZipDePrueba.PedirAsync(cliente, deB.Id, ct);
        pedido.StatusCode.Should().Be(HttpStatusCode.NotFound);
        cuerpoPedido.GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
        foreach (var ruta in new[]
                 {
                     ZipDePrueba.RutaEstado(deB.Id, eidB), ZipDePrueba.RutaArchivo(deB.Id, eidB),
                     ZipDePrueba.RutaEstado(deA.Id, eidB), ZipDePrueba.RutaArchivo(deA.Id, eidB),
                 })
        {
            var respuesta = await cliente.GetAsync(ruta, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{ruta}: {cuerpo}");
            JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
            cuerpo.Should().NotContainAny(deBNoCruza);
        }

        var propio = await ZipDePrueba.ArchivoOkAsync(cliente, deA.Id, eidA, ct);
        propio.Texto.Should().Contain("TST562").And.NotContainAny(deBNoCruza.Select(PdfTextExtractor.Flatten).Concat(deBNoCruza));

        await PropiosAsync(empresas.A, ct);
        var otroDeA = api.Cliente(PdfDePrueba.Token(api, empresas.A.Id));
        (await ZipDePrueba.PedirAsync(otroDeA, deA.Id, ct)).Respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, "PROPIOS: el lote es de otro usuario");
        (await otroDeA.GetAsync(ZipDePrueba.RutaEstado(deA.Id, eidA), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otroDeA.GetAsync(ZipDePrueba.RutaArchivo(deA.Id, eidA), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ZipsAsync(empresas.A, deB.Id, ct)).Should().BeEmpty("A no ve filas de B");
    }

    // F07-C8 (reutilización) — el PDF del vehículo ya descargado con la misma huella se reutiliza: el ZIP trae esos mismos bytes y
    // no se crea otra evidencia PDF para el vehículo.
    [Fact]
    public async Task Reutiliza_el_pdf_vigente_del_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteTerminadoAsync(empresa, "TST571", ct);
        var vehiculo = lote.Vehiculos.Single().Id;
        var cliente = api.Cliente(PdfDePrueba.Token(api, empresa.Id));
        var pdf = await PdfDePrueba.BytesOkAsync(cliente, lote.Id, vehiculo, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(1100), ct);

        var (_, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
        await ZipDePrueba.DrenarAsync(api, ct);
        var zip = await ZipDePrueba.ArchivoOkAsync(cliente, lote.Id, pedido.GetProperty("id").GetGuid(), ct);

        zip.Entradas[zip.Pdfs.Single()].Should().Equal(pdf, "misma huella: el PDF vigente, sin regenerar");
        await using var db = empresa.Db();
        (await db.Set<Evidencia>().CountAsync(e => e.VehiculoId == vehiculo, ct)).Should().Be(1);
    }

    private static VehiculoSembradoZip Vehiculo(string placa) => new(placa);

    private static string Runt(string placa) => PayloadRunt.Replace("TST501", placa, StringComparison.Ordinal);

    /// <summary>Lote EN_PROCESO con los vehículos dados terminados (RUNT y SIMIT_VEHICULO COMPLETADO).</summary>
    private static async Task<Tablero.LoteSembrado> LoteTerminadoAsync(
        EmpresaDePrueba empresa, string placa, CancellationToken ct, string? otra = null, string nombre = "Lote del ZIP", Guid? creador = null)
    {
        string[] placas = otra is null ? [placa] : [placa, otra];
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, alcance: [Fuente.Runt, Fuente.SimitVehiculo], nombre: nombre, creador: creador,
            vehiculos: [.. placas.Select(Vehiculo)]);
        foreach (var p in placas)
            await CompletarAsync(empresa, lote, p, ct);
        return lote;
    }

    private static async Task CompletarAsync(EmpresaDePrueba empresa, Tablero.LoteSembrado lote, string placa, CancellationToken ct)
    {
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.Runt), Runt(placa), ct);
        await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.SimitVehiculo), PayloadSimit, ct);
    }

    /// <summary>Llenado manual de una celda (HU #13800): nuevo payload, origen MANUAL, nueva fecha y la versión del tablero sube.</summary>
    private static async Task LlenarAManoAsync(EmpresaDePrueba empresa, Guid celda, string payload, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var fila = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == celda, ct);
        fila.Payload = payload;
        fila.Origen = OrigenCelda.Manual;
        fila.UsuarioManualId = Guid.NewGuid();
        fila.DatoObtenidoEn = DateTimeOffset.UtcNow;
        fila.VersionTablero += 1;
        await db.SaveChangesAsync(ct);
    }

    private static async Task MismaFechaAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).ExecuteUpdateAsync(s => s.SetProperty(c => c.DatoObtenidoEn, Dato), ct);
    }

    private static async Task PropiosAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<Evidencia>> ZipsAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Evidencia>().AsNoTracking().Where(e => e.DiagnosticoId == lote && e.Tipo == TipoEvidencia.Zip)
            .OrderBy(e => e.CreadoEn).ThenBy(e => e.Id).ToListAsync(ct);
    }

    private static async Task<Evidencia> EvidenciaAsync(EmpresaDePrueba empresa, Guid id, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Evidencia>().AsNoTracking().SingleAsync(e => e.Id == id, ct);
    }

    /// <summary>Vehículo con RUNT y SIMIT_VEHICULO en PENDIENTE (la prueba los completa con su payload).</summary>
    private sealed record VehiculoSembradoZip(string Placa)
    {
        public static implicit operator Tablero.VehiculoSembrado(VehiculoSembradoZip v) =>
            new(v.Placa, [new(Fuente.Runt, EstadoCelda.Pendiente, 1), new(Fuente.SimitVehiculo, EstadoCelda.Pendiente, 1)]);
    }
}

/// <summary>
/// Ayudas del ZIP del lote (HU #13850): rutas, pedido, estado, el empaquetador de la cola (el trabajador hospedado está apagado
/// en las pruebas) y la lectura del ZIP: entradas, <c>indice.txt</c> y el texto aplanado de cada PDF. También es el lector del
/// cuerpo del escenario de fuga (<see cref="EscenariosDeFuga.RutaArchivoEvidencia"/>): un cuerpo que no es ZIP se devuelve tal cual.
/// </summary>
/// <example>
/// <code>
/// var (respuesta, pedido) = await ZipDePrueba.PedirAsync(cliente, lote.Id, ct);
/// await ZipDePrueba.DrenarAsync(api, ct);
/// var zip = await ZipDePrueba.ArchivoOkAsync(cliente, lote.Id, pedido.GetProperty("id").GetGuid(), ct);
/// </code>
/// </example>
internal static class ZipDePrueba
{
    public const string ContentType = "application/zip";

    public static string Ruta(Guid id) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/evidencias";

    public static string RutaEstado(Guid id, Guid eid) => $"{Ruta(id)}/{eid}";

    public static string RutaArchivo(Guid id, Guid eid) => $"{RutaEstado(id, eid)}/archivo";

    /// <summary>La API con el resolutor de nombres sobre una Identidad de prueba y lo que la prueba agregue.</summary>
    public static Task<ApiDePrueba> ArrancarAsync(Action<WebApplicationBuilder>? configurar = null) =>
        ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.AddSingleton<IResolutorNombresUsuario>(new UsuariosIdentidadDePrueba().Resolutor());
            configurar?.Invoke(b);
        });

    public static async Task<(HttpResponseMessage Respuesta, JsonElement Cuerpo)> PedirAsync(HttpClient cliente, Guid id, CancellationToken ct, string tipo = "ZIP")
    {
        ArgumentNullException.ThrowIfNull(cliente);
        var respuesta = await cliente.PostAsJsonAsync(Ruta(id), new Dictionary<string, string> { ["tipo"] = tipo }, ct);
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        return (respuesta, texto.Length == 0 ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    public static async Task<JsonElement> EstadoAsync(HttpClient cliente, Guid id, Guid eid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        var respuesta = await cliente.GetAsync(RutaEstado(id, eid), ct);
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    /// <summary>Una vuelta del empaquetador: true si tomó un ZIP de la cola.</summary>
    public static Task<bool> ProcesarAsync(ApiDePrueba api, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api);
        return api.App.Services.GetRequiredService<EmpaquetadorZip>().ProcesarSiguienteAsync(ct);
    }

    /// <summary>Procesa la cola hasta vaciarla; devuelve cuántos ZIP tomó.</summary>
    public static async Task<int> DrenarAsync(ApiDePrueba api, CancellationToken ct)
    {
        var tomados = 0;
        while (await ProcesarAsync(api, ct))
            tomados++;
        return tomados;
    }

    public static async Task<ZipLeido> ArchivoOkAsync(HttpClient cliente, Guid id, Guid eid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        var respuesta = await cliente.GetAsync(RutaArchivo(id, eid), ct);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, Encoding.UTF8.GetString(bytes));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be(ContentType);
        return Leer(bytes);
    }

    /// <summary>El contenido del ZIP como texto (nombres, índice y texto de cada PDF); si la respuesta no es un ZIP, el cuerpo tal cual.</summary>
    public static async Task<string> TextoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (respuesta.Content.Headers.ContentType?.MediaType != ContentType)
            return await respuesta.Content.ReadAsStringAsync(ct);
        return Leer(await respuesta.Content.ReadAsByteArrayAsync(ct)).Texto;
    }

    public static ZipLeido Leer(byte[] zip)
    {
        using var flujo = new MemoryStream(zip);
        using var archivo = new ZipArchive(flujo, ZipArchiveMode.Read);
        var entradas = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entrada in archivo.Entries)
        {
            using var contenido = entrada.Open();
            using var copia = new MemoryStream();
            contenido.CopyTo(copia);
            entradas[entrada.FullName] = copia.ToArray();
        }

        return new ZipLeido(entradas);
    }
}

/// <summary>Las entradas de un ZIP leído: los PDF, el índice y su texto.</summary>
internal sealed record ZipLeido(IReadOnlyDictionary<string, byte[]> Entradas)
{
    public IReadOnlyList<string> Pdfs => [.. Entradas.Keys.Where(n => n.EndsWith(".pdf", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    public string Indice => Entradas.TryGetValue("indice.txt", out var bytes) ? Encoding.UTF8.GetString(bytes) : string.Empty;

    /// <summary>Texto aplanado del PDF cuyo nombre contiene <paramref name="placaONombre"/>.</summary>
    public string TextoPdf(string placaONombre) =>
        PdfDePrueba.Texto(Entradas[Pdfs.Single(n => n.Contains(placaONombre, StringComparison.Ordinal))]);

    /// <summary>Nombres de las entradas, el índice y el texto de cada PDF, uno por línea.</summary>
    public string Texto => string.Join('\n', Entradas.Keys.Append(Indice).Concat(Pdfs.Select(n => PdfDePrueba.Texto(Entradas[n]))));
}

/// <summary>
/// Almacén cuyo guardado del ZIP siempre falla (HU #13850): los PDF van a la base como de costumbre; cuenta los intentos del ZIP.
/// </summary>
internal sealed class FabricaAlmacenQueFallaEnZip : IFabricaAlmacen
{
    private int _intentosZip;

    public int IntentosZip => _intentosZip;

    public IAlmacenArchivos Para(DiagnosticoDb db) => new Almacen(this, new AlmacenEnBase(db));

    private sealed class Almacen(FabricaAlmacenQueFallaEnZip fabrica, IAlmacenArchivos interno) : IAlmacenArchivos
    {
        public Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
        {
            if (tipo != "ZIP")
                return interno.GuardarAsync(empresaId, tipo, nombre, contentType, contenido, ct);
            Interlocked.Increment(ref fabrica._intentosZip);
            throw new HttpRequestException("file-manager de prueba caído");
        }

        public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) => interno.LeerAsync(referencia, ct);
    }
}
