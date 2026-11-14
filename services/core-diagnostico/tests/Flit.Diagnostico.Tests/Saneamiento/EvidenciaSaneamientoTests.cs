using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Evidencia en PDF de un renglón (plan §6.4, ADR-0087 D-6; HU #14576 AC45–AC47; seguridad SP-12, SP-16, SP-17, SP-18 y SP-19):
/// subida y reemplazo desde el backoffice con el tope de tamaño antes de leer, firma exacta, nombre generado por el servidor, tope
/// de reemplazos, descarga del cliente y de FLIT con las cabeceras seguras, y borrado. PostgreSQL real con el almacén en la base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EvidenciaSaneamientoTests
/// </code>
/// </example>
public sealed class EvidenciaSaneamientoTests
{
    private const int Maximo = 10_485_760;

    // AC45 + SP-12 — sube y reemplaza: el almacén recibe un nombre generado por el servidor y la empresa del lote; el renglón guarda
    // referencia, sha, tamaño y quién; el historial suma EVIDENCIA_CARGADA y el reemplazo deja el anterior huérfano.
    [Fact]
    public async Task HU14576_AC45_SP12_subir_y_reemplazar_la_evidencia()
    {
        var almacen = new FabricaQueRegistra();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Con(almacen));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBE101", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Saneado, ct);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api, usuario: flit));
        var ruta = GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia");

        var primera = await cliente.PutAsync(ruta, GestionDePrueba.Archivo(GestionDePrueba.PdfValido, "..\\..\\carpeta/pago‮.pdf"), ct);
        Guardado(await LeerRenglonAsync(empresa, renglon.Id, ct), out var referencia1);
        var segunda = await cliente.PutAsync(ruta, GestionDePrueba.Archivo([.. GestionDePrueba.PdfValido, .. "%%EOF\n"u8.ToArray()], "segundo.pdf"), ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Content.ReadAsStringAsync(ct));
        var json = await GestionDePrueba.JsonAsync(primera, ct);
        json.GetProperty("id").GetGuid().Should().Be(renglon.Id);
        json.GetProperty("evidencia").GetProperty("nombre").GetString().Should().Be("pago.pdf", "sin rutas ni marcas bidireccionales");
        json.GetProperty("evidencia").GetProperty("tamano").GetInt64().Should().Be(GestionDePrueba.PdfValido.Length);
        almacen.Llamadas.Should().HaveCount(2);
        almacen.Llamadas.Should().OnlyContain(l => System.Text.RegularExpressions.Regex.IsMatch(l.Nombre, "^evidencia-[0-9a-f]{32}\\.pdf$") && l.Empresa == empresa.Id);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, await segunda.Content.ReadAsStringAsync(ct));
        var fila = await LeerRenglonAsync(empresa, renglon.Id, ct);
        Guardado(fila, out var referencia2);
        referencia2.Should().NotBe(referencia1, "el renglón apunta al nuevo");
        fila.EvidenciaNombre.Should().Be("segundo.pdf");
        fila.EvidenciaSubidaPor.Should().Be(flit);
        fila.EvidenciaReemplazos.Should().Be(1);
        await using (var db = empresa.Db())
            (await db.Set<ArchivoLocal>().CountAsync(ct)).Should().Be(2, "el anterior queda huérfano, sin borrarse");
        (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Count(e => e.Evento == TipoEventoSaneamiento.EvidenciaCargada && e.RenglonId == renglon.Id).Should().Be(2);
    }

    // AC46 + SP-16 + SP-17 — archivo inválido: el renglón no cambia.
    [Theory]
    [InlineData("docx", 422, "ARCHIVO_NO_ES_PDF")]
    [InlineData("png", 422, "ARCHIVO_NO_ES_PDF")]
    [InlineData("octet", 422, "ARCHIVO_NO_ES_PDF")]
    [InlineData("espacio", 422, "ARCHIVO_NO_ES_PDF")]
    [InlineData("html", 422, "ARCHIVO_NO_ES_PDF")]
    [InlineData("sin_eof", 422, "ARCHIVO_NO_ES_PDF")]
    [InlineData("extension", 422, "ARCHIVO_NO_ES_PDF")]
    [InlineData("grande", 413, "ARCHIVO_MUY_GRANDE")]
    [InlineData("vacio", 400, "ARCHIVO_REQUERIDO")]
    [InlineData("otro_campo", 400, "ARCHIVO_REQUERIDO")]
    [InlineData("dos", 400, "ARCHIVO_REQUERIDO")]
    [InlineData("saneado", 409, "NO_EDITABLE")]
    public async Task HU14576_AC46_rechazos_de_la_evidencia(string caso, int estado, string codigo)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBE102", caso == "saneado" ? EstadoSaneamiento.Saneado : EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Soat, ResultadoRenglonSaneamiento.Pendiente, ct);
        var pdf = GestionDePrueba.PdfValido;
        var contenido = caso switch
        {
            "docx" => GestionDePrueba.Archivo([0x50, 0x4B, 0x03, 0x04, .. "%%EOF"u8.ToArray()], "pago.pdf"),
            "png" => GestionDePrueba.Archivo(pdf, "pago.pdf", "image/png"),
            "octet" => GestionDePrueba.Archivo(pdf, "pago.pdf", "application/octet-stream"),
            "espacio" => GestionDePrueba.Archivo([.. " "u8.ToArray(), .. pdf]),
            "html" => GestionDePrueba.Archivo([.. "<html><body></body></html>%PDF-1.7\n"u8.ToArray(), .. pdf]),
            "sin_eof" => GestionDePrueba.Archivo("%PDF-1.7\n1 0 obj<<>>endobj\n"u8.ToArray()),
            "extension" => GestionDePrueba.Archivo(pdf, "pago.exe"),
            "grande" => GestionDePrueba.Archivo(PdfDe(Maximo + 1)),
            "vacio" => GestionDePrueba.Archivo([]),
            "otro_campo" => GestionDePrueba.Archivo(pdf, campo: "documento"),
            "dos" => Dos(pdf),
            _ => GestionDePrueba.Archivo(pdf),
        };

        var respuesta = await api.Cliente(GestionDePrueba.TokenFlit(api)).PutAsync(GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia"), contenido, ct);

        ((int)respuesta.StatusCode).Should().Be(estado, await respuesta.Content.ReadAsStringAsync(ct));
        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be(codigo);
        (await LeerRenglonAsync(empresa, renglon.Id, ct)).EvidenciaReferencia.Should().BeNull("el renglón no cambia");
    }

    // SP-16 — exactamente el máximo se acepta; 40 MB se rechaza antes de leer el cuerpo (el almacén no se llama).
    [Fact]
    public async Task HU14576_SP16_el_tope_actua_antes_de_leer_el_cuerpo()
    {
        var almacen = new FabricaQueRegistra();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Con(almacen));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBE103", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Soat, ResultadoRenglonSaneamiento.Pendiente, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        var ruta = GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia");

        var enorme = await cliente.PutAsync(ruta, GestionDePrueba.Archivo(PdfDe(40 * 1024 * 1024)), ct);
        almacen.Llamadas.Should().BeEmpty("40 MB se rechaza sin llegar al almacén");
        var justo = await cliente.PutAsync(ruta, GestionDePrueba.Archivo(PdfDe(Maximo)), ct);

        enorme.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await SaneamientoDePrueba.CodigoAsync(enorme, ct)).Should().Be("ARCHIVO_MUY_GRANDE");
        justo.StatusCode.Should().Be(HttpStatusCode.OK, await justo.Content.ReadAsStringAsync(ct));
        almacen.Llamadas.Should().ContainSingle().Which.Tamano.Should().Be(Maximo);
    }

    // SP-19 — con 20 reemplazos el siguiente es 409 DEMASIADOS_REEMPLAZOS y el renglón no cambia.
    [Fact]
    public async Task HU14576_SP19_tope_de_veinte_reemplazos()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBE104", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Rtm, ResultadoRenglonSaneamiento.Saneado, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        var ruta = GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia");
        (await cliente.PutAsync(ruta, GestionDePrueba.Archivo(GestionDePrueba.PdfValido), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = empresa.Db())
            await db.Set<RenglonSaneamiento>().Where(r => r.Id == renglon.Id).ExecuteUpdateAsync(x => x.SetProperty(r => r.EvidenciaReemplazos, (short)20), ct);
        var antes = await LeerRenglonAsync(empresa, renglon.Id, ct);

        var respuesta = await cliente.PutAsync(ruta, GestionDePrueba.Archivo(GestionDePrueba.PdfValido, "otro.pdf"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("DEMASIADOS_REEMPLAZOS");
        (await LeerRenglonAsync(empresa, renglon.Id, ct)).EvidenciaReferencia.Should().Be(antes.EvidenciaReferencia);
    }

    // AC47 + SP-18 — el cliente con reportes.descargar y FLIT descargan con el nombre evidencia-{placa}-{fuente}.pdf y las cuatro
    // cabeceras; sin evidencia es 404; quitarla deja las seis columnas nulas y suma EVIDENCIA_QUITADA.
    [Fact]
    public async Task HU14576_AC47_descargar_y_quitar_la_evidencia()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "ABC123", EstadoSaneamiento.EnGestion, ct);
        var conEvidencia = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Saneado, ct);
        var sinEvidencia = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Soat, ResultadoRenglonSaneamiento.Pendiente, ct);
        using var flit = api.Cliente(GestionDePrueba.TokenFlit(api));
        (await flit.PutAsync(GestionDePrueba.RutaRenglon(s.Id, conEvidencia.Id, empresa.Id, "/evidencia"), GestionDePrueba.Archivo(GestionDePrueba.PdfValido, "recibo-original.pdf"), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var vehiculo = lote.Vehiculos[0].Id;
        string RutaCliente(Guid rid) => $"{SaneamientoDePrueba.Ruta(lote.Id, vehiculo)}/renglones/{rid}/evidencia";
        var descarga = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ReportesDescargar], roles: ["usuario_diagnostico"]);
        var soloVer = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);

        var delCliente = await api.Cliente(descarga).GetAsync(RutaCliente(conEvidencia.Id), ct);
        var deFlit = await flit.GetAsync(GestionDePrueba.RutaRenglon(s.Id, conEvidencia.Id, empresa.Id, "/evidencia"), ct);
        var sinPermiso = await api.Cliente(soloVer).GetAsync(RutaCliente(conEvidencia.Id), ct);
        var vacia = await api.Cliente(descarga).GetAsync(RutaCliente(sinEvidencia.Id), ct);
        var vaciaFlit = await flit.GetAsync(GestionDePrueba.RutaRenglon(s.Id, sinEvidencia.Id, empresa.Id, "/evidencia"), ct);

        foreach (var respuesta in new[] { delCliente, deFlit })
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
            respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            DeudaDePrueba.CabecerasSeguras(respuesta);
            respuesta.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("evidencia-ABC123-IMPUESTO.pdf");
            respuesta.Content.Headers.ContentDisposition.ToString().Should().NotContain("recibo-original");
            (await respuesta.Content.ReadAsByteArrayAsync(ct)).Should().Equal(GestionDePrueba.PdfValido);
        }

        sinPermiso.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        vacia.StatusCode.Should().Be(HttpStatusCode.NotFound);
        vaciaFlit.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var quitar = await flit.DeleteAsync(GestionDePrueba.RutaRenglon(s.Id, conEvidencia.Id, empresa.Id, "/evidencia"), ct);

        quitar.StatusCode.Should().Be(HttpStatusCode.OK, "D-65: devuelve el saneamiento completo");
        (await GestionDePrueba.JsonAsync(quitar, ct)).GetProperty("renglones").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == conEvidencia.Id).GetProperty("evidencia").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        var fila = await LeerRenglonAsync(empresa, conEvidencia.Id, ct);
        new object?[] { fila.EvidenciaReferencia, fila.EvidenciaNombre, fila.EvidenciaSha256, fila.EvidenciaTamano, fila.EvidenciaSubidaPor, fila.EvidenciaSubidaEn }
            .Should().OnlyContain(v => v == null);
        (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Should().ContainSingle(e => e.Evento == TipoEventoSaneamiento.EvidenciaQuitada && e.RenglonId == conEvidencia.Id);
        (await api.Cliente(descarga).GetAsync(RutaCliente(conEvidencia.Id), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // SP-19 + CR-3 — quitar la evidencia no reinicia el tope: cada carga después de la primera suma, también la que llega a un
    // renglón sin evidencia porque se quitó. Tras la primera carga, 20 ciclos DELETE + PUT llegan al tope (CHECK 0..20) y la carga
    // número 22 responde 409 DEMASIADOS_REEMPLAZOS sin tocar el renglón.
    [Fact]
    public async Task HU14576_SP19_CR3_DELETE_y_PUT_alternados_tambien_cuentan_hasta_el_tope()
    {
        var almacen = new FabricaQueRegistra();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Con(almacen));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBE105", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Soat, ResultadoRenglonSaneamiento.Saneado, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        var ruta = GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia");
        (await cliente.PutAsync(ruta, GestionDePrueba.Archivo(GestionDePrueba.PdfValido), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerRenglonAsync(empresa, renglon.Id, ct)).EvidenciaReemplazos.Should().Be(0, "la primera carga no es un reemplazo");

        var cuentas = new List<short>();
        for (var ciclo = 1; ciclo <= EvidenciaTope; ciclo++)
        {
            (await cliente.DeleteAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"quitar en el ciclo {ciclo}");
            var carga = await cliente.PutAsync(ruta, GestionDePrueba.Archivo(GestionDePrueba.PdfValido, $"ciclo-{ciclo}.pdf"), ct);
            carga.StatusCode.Should().Be(HttpStatusCode.OK, $"la carga del ciclo {ciclo}: {await carga.Content.ReadAsStringAsync(ct)}");
            cuentas.Add((await LeerRenglonAsync(empresa, renglon.Id, ct)).EvidenciaReemplazos);
        }

        (await cliente.DeleteAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK, "quitar sigue permitido en el tope");
        var guardadosAntes = almacen.Llamadas.Count;
        var antes = await LeerRenglonAsync(empresa, renglon.Id, ct);
        var excedida = await cliente.PutAsync(ruta, GestionDePrueba.Archivo(GestionDePrueba.PdfValido, "excedida.pdf"), ct);

        cuentas.Should().Equal(Enumerable.Range(1, EvidenciaTope).Select(n => (short)n), "cada carga después de la primera suma uno");
        excedida.StatusCode.Should().Be(HttpStatusCode.Conflict, await excedida.Content.ReadAsStringAsync(ct));
        (await SaneamientoDePrueba.CodigoAsync(excedida, ct)).Should().Be("DEMASIADOS_REEMPLAZOS");
        almacen.Llamadas.Count.Should().Be(guardadosAntes, "el tope actúa antes de guardar el archivo: no deja otro huérfano");
        var despues = await LeerRenglonAsync(empresa, renglon.Id, ct);
        (despues.EvidenciaReferencia, despues.EvidenciaReemplazos, despues.Xmin).Should().Be((antes.EvidenciaReferencia, antes.EvidenciaReemplazos, antes.Xmin));
        despues.EvidenciaReemplazos.Should().Be(EvidenciaTope);
    }

    // AC47 + CR-10 — las dos descargas (cliente y FLIT) salen en stream desde IAlmacenArchivos.AbrirAsync, nunca con LeerAsync (que
    // arma el archivo entero en memoria); el flujo se libera al terminar la respuesta y se conservan las cabeceras de DescargaSegura.
    [Fact]
    public async Task HU14576_AC47_CR10_las_descargas_salen_en_stream_desde_el_almacen()
    {
        var almacen = new FabricaQueRegistra();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Con(almacen));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBE106", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Saneado, ct);
        using var flit = api.Cliente(GestionDePrueba.TokenFlit(api));
        var rutaFlit = GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia");
        (await flit.PutAsync(rutaFlit, GestionDePrueba.Archivo(GestionDePrueba.PdfValido), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var descarga = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ReportesDescargar], roles: ["usuario_diagnostico"]);
        var rutaCliente = $"{SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id)}/renglones/{renglon.Id}/evidencia";

        var delCliente = await api.Cliente(descarga).GetAsync(rutaCliente, ct);
        var deFlit = await flit.GetAsync(rutaFlit, ct);

        foreach (var respuesta in new[] { delCliente, deFlit })
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
            respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            DeudaDePrueba.CabecerasSeguras(respuesta);
            respuesta.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
            respuesta.Content.Headers.ContentDisposition.FileNameStar.Should().Be("evidencia-SBE106-IMPUESTO.pdf");
            (await respuesta.Content.ReadAsByteArrayAsync(ct)).Should().Equal(GestionDePrueba.PdfValido);
        }

        almacen.Lecturas.Should().Be(0, "ninguna descarga arma el PDF entero en memoria con LeerAsync");
        almacen.Aperturas.Should().HaveCount(2, "cliente y FLIT abren el archivo como flujo");
        almacen.Aperturas.Should().OnlyContain(f => f.Liberado, "el flujo se libera al terminar la respuesta");
    }

    // AP-3 — sin placa ni identificador, el nombre es evidencia-sin-placa-{fuente}.pdf, sin espacios.
    [Theory]
    [InlineData(null, FuenteSaneamiento.Impuesto, "evidencia-sin-placa-IMPUESTO.pdf")]
    [InlineData("", FuenteSaneamiento.Soat, "evidencia-sin-placa-SOAT.pdf")]
    [InlineData("  -- ", FuenteSaneamiento.Rtm, "evidencia-sin-placa-RTM.pdf")]
    [InlineData("Sin placa", FuenteSaneamiento.Impuesto, "evidencia-SINPLACA-IMPUESTO.pdf")]
    [InlineData("abc 123", FuenteSaneamiento.Impuesto, "evidencia-ABC123-IMPUESTO.pdf")]
    public void HU14576_AP3_nombre_de_la_evidencia_sin_placa(string? placa, FuenteSaneamiento fuente, string esperado)
    {
        var nombre = Flit.Diagnostico.Api.Modulos.Backoffice.EvidenciaSaneamiento.NombreDescarga(placa, fuente);

        nombre.Should().Be(esperado);
        nombre.Should().NotContain(" ");
    }

    private const int EvidenciaTope = 20;

    private static void Guardado(RenglonSaneamiento fila, out string referencia)
    {
        fila.EvidenciaReferencia.Should().NotBeNullOrEmpty();
        fila.EvidenciaSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        fila.EvidenciaTamano.Should().BeGreaterThan(0);
        fila.EvidenciaSubidaEn.Should().NotBeNull();
        referencia = fila.EvidenciaReferencia!;
    }

    private static async Task<RenglonSaneamiento> LeerRenglonAsync(EmpresaDePrueba empresa, Guid rid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<RenglonSaneamiento>().AsNoTracking().SingleAsync(r => r.Id == rid, ct);
    }

    /// <summary>Un PDF válido de exactamente <paramref name="tamano"/> bytes (relleno entre la cabecera y el %%EOF).</summary>
    private static byte[] PdfDe(int tamano)
    {
        var inicio = "%PDF-1.7\n"u8.ToArray();
        var fin = "\n%%EOF\n"u8.ToArray();
        var bytes = new byte[tamano];
        Array.Fill(bytes, (byte)' ');
        inicio.CopyTo(bytes, 0);
        fin.CopyTo(bytes, tamano - fin.Length);
        return bytes;
    }

    private static MultipartFormDataContent Dos(byte[] pdf)
    {
        var multipart = GestionDePrueba.Archivo(pdf);
        var otra = new ByteArrayContent(pdf);
        otra.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        multipart.Add(otra, "archivo", "otro.pdf");
        return multipart;
    }

    private static Action<WebApplicationBuilder> Con(FabricaQueRegistra fabrica) => b => b.Services.AddSingleton<IFabricaAlmacen>(fabrica);

    /// <summary>
    /// El almacén en la base, que además registra cada guardado (empresa, nombre y tamaño, sin el contenido), cuántas veces se leyó
    /// el archivo entero (<see cref="IAlmacenArchivos.LeerAsync"/>) y cada flujo abierto (<see cref="IAlmacenArchivos.AbrirAsync"/>) con
    /// si se liberó.
    /// </summary>
    private sealed class FabricaQueRegistra : IFabricaAlmacen
    {
        private int _lecturas;

        public ConcurrentQueue<(Guid Empresa, string Nombre, long Tamano)> Llamadas { get; } = new();

        public ConcurrentQueue<FlujoRegistrado> Aperturas { get; } = new();

        public int Lecturas => Volatile.Read(ref _lecturas);

        public IAlmacenArchivos Para(DiagnosticoDb db) => new Almacen(new AlmacenEnBase(db), this);

        private sealed class Almacen(IAlmacenArchivos real, FabricaQueRegistra registro) : IAlmacenArchivos
        {
            public Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
            {
                registro.Llamadas.Enqueue((empresaId, nombre, contenido.LongLength));
                return real.GuardarAsync(empresaId, tipo, nombre, contentType, contenido, ct);
            }

            public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct)
            {
                Interlocked.Increment(ref registro._lecturas);
                return real.LeerAsync(referencia, ct);
            }

            public async Task<Stream?> AbrirAsync(string referencia, CancellationToken ct)
            {
                if (await real.LeerAsync(referencia, ct) is not { } bytes)
                    return null;
                var flujo = new FlujoRegistrado(bytes);
                registro.Aperturas.Enqueue(flujo);
                return flujo;
            }
        }
    }

    /// <summary>Un flujo de solo lectura que recuerda si se liberó.</summary>
    private sealed class FlujoRegistrado(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool Liberado { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Liberado = true;
            base.Dispose(disposing);
        }
    }
}
