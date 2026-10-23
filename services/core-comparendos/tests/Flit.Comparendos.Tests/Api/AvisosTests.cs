using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13485 — avisos 1 a 3 al conductor. Fotomulta notificada el 1-oct-2026: 50% hasta el 19-oct, 25% hasta el 10-nov.</summary>
public sealed class AvisosTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.AvisosEnviar, Permisos.PagosRegistrar];

    private async Task<(Guid Empresa, Dictionary<string, Guid> Ids)> PrepararAsync(params string[] numeros)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var ids = await IngresarAsync(api, empresa, numeros.Select(n => Item(n)).ToArray());
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        foreach (var id in ids.Values)
        {
            (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
            {
                tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@example.test", declaracionTratamientoDatos = true,
            })).EnsureSuccessStatusCode();
        }

        return (empresa, ids);
    }

    private async Task SchedulerAsync(Guid empresa)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<SchedulerAvisos>().Single();
        await ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.AvisosConductor, Disparo.Manual, empresa), CancellationToken.None);
    }

    private Task<List<AvisoDto>> AvisosAsync(Guid id) =>
        api.EnDbAsync(db => db.AvisosConductor.Where(a => a.ComparendoId == id).OrderBy(a => a.RegistradoEn).Select(a => new AvisoDto(a.TipoAviso, a.VentanaAlEnvio, a.PorcentajeDescuento)).ToListAsync());

    private sealed record AvisoDto(TipoAviso Tipo, VentanaDescuento Ventana, short Porcentaje);

    [Fact]
    public async Task AC1_CicloCompleto_Aviso1_Aviso2_Aviso3_SinRetroactividad()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AV-1");
        var id = ids["AV1"];

        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        (await AvisosAsync(id)).Should().Equal(new AvisoDto(TipoAviso.Aviso1, VentanaDescuento.Descuento50, 50));
        var c = await api.EnDbAsync(db => db.Comparendos.SingleAsync(x => x.Id == id));
        c.EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso1);
        (await api.EnDbAsync(db => db.PortalEnlaces.CountAsync(p => p.ComparendoId == id))).Should().Be(1);
        var correo = await api.EnDbAsync(db => db.Comunicaciones.SingleAsync(x => x.ComparendoId == id));
        correo.Asunto.Should().Be("Comparendo AV-1 del vehículo ABC124: tienes descuento");
        correo.Tipo.Should().Be(TipoComunicacion.Aviso1);

        api.Reloj.Hoy(new DateOnly(2026, 10, 20));
        await SchedulerAsync(empresa);
        api.Reloj.Hoy(new DateOnly(2026, 11, 11));
        await SchedulerAsync(empresa);

        (await AvisosAsync(id)).Select(a => (a.Tipo, a.Porcentaje)).Should().Equal((TipoAviso.Aviso1, (short)50), (TipoAviso.Aviso2, (short)25), (TipoAviso.Aviso3, (short)0));
    }

    [Fact]
    public async Task AC2_Pagado_NoRecibeAvisos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AV-2");
        await api.EnDbAsync(db => db.Comparendos.Where(x => x.Id == ids["AV2"]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Pagado, true)));

        await SchedulerAsync(empresa);

        (await AvisosAsync(ids["AV2"])).Should().BeEmpty();
        var r = await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["AV2"]}/avisos", new { tipo = "Aviso1" }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("error").GetString().Should().Be("El comparendo ya está pagado.");
    }

    [Fact]
    public async Task Manual_SeEnviaYSeDespachaAlMomento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AV-3");
        var r = await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["AV3"]}/avisos", new { tipo = "Aviso2" }, TestContext.Current.CancellationToken);

        (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("estado").GetString().Should().Be("Enviado");
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(x => x.Id == ids["AV3"]))).EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso2);
    }

    [Theory]
    [InlineData("07:00", "07:00", true)]
    [InlineData("07:00", "07:14", true)]
    [InlineData("07:00", "07:15", false)]
    [InlineData("07:00", "06:59", false)]
    public void EnTramo(string hora, string ahora, bool esperado) =>
        SchedulerAvisos.EnTramo(TimeOnly.Parse(hora, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(ahora, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(esperado);

    [Fact]
    public async Task S33_ConductorReemplazado_RecibeSusPropiosAvisosAutomaticos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, ids) = await PrepararAsync("AV-R");
        var id = ids["AVR"];
        await SchedulerAsync(empresa);
        (await AvisosAsync(id)).Should().ContainSingle();

        (await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "79888777", nombres = "Ana Ruiz", correo = "ana@example.test", motivo = "El contrato era de otra conductora", declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        api.Reloj.Hoy(new DateOnly(2026, 10, 13));
        await SchedulerAsync(empresa);
        var destinos = await api.EnDbAsync(db => db.AvisosConductor.Where(a => a.ComparendoId == id && a.TipoAviso == TipoAviso.Aviso1).Select(a => a.CorreoDestino).ToListAsync(ct));
        destinos.Should().BeEquivalentTo("juan@example.test", "ana@example.test");
    }

    [Fact]
    public async Task D7_LaFotoDeLaFotomulta_VaAdjunta_YSiNoSePuedeTraer_ElAvisoSaleIgual()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        api.Fotos.PorUrl["https://simit.test/foto/AV-F1.jpg"] = [0xFF, 0xD8, 0xFF, 0xE0];
        var ids = await IngresarAsync(api, empresa,
            Item("AV-F1") with { FotoEvidenciaUrl = "https://simit.test/foto/AV-F1.jpg" },
            Item("AV-F2") with { FotoEvidenciaUrl = "https://simit.test/foto/no-existe.jpg" });
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        foreach (var id in ids.Values)
        {
            (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
            {
                tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@example.test", declaracionTratamientoDatos = true,
            }, ct)).EnsureSuccessStatusCode();
        }

        await SchedulerAsync(empresa);

        async Task<List<string>> Adjuntos(Guid id) => await api.EnDbAsync(db =>
            (from c in db.Comunicaciones where c.ComparendoId == id join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId select a.Nombre).ToListAsync(ct));
        (await Adjuntos(ids["AVF1"])).Should().Equal("fotomulta-AV-F1.jpg");
        (await AvisosAsync(ids["AVF2"])).Should().ContainSingle("sin la foto el aviso sale igual");
        (await Adjuntos(ids["AVF2"])).Should().BeEmpty();
    }

    [Fact]
    public async Task E33_LaPrimeraApertura_QuedaEnElTimeline_UnaSolaVez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, ids) = await PrepararAsync("AV-P1");
        var id = ids["AVP1"];
        await SchedulerAsync(empresa);
        var correo = await api.EnDbAsync(db => db.Comunicaciones.SingleAsync(x => x.ComparendoId == id, ct));

        for (var i = 0; i < 2; i++)
            (await api.Anonimo().GetAsync($"/api/v1/comparendos/publico/t/{correo.Id:N}.gif", ct)).EnsureSuccessStatusCode();

        (await api.EnDbAsync(db => db.Comunicaciones.SingleAsync(x => x.Id == correo.Id, ct))).Aperturas.Should().Be(2);
        var eventos = await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.ComunicacionEstado && t.RelacionadoId == correo.Id).ToListAsync(ct));
        eventos.Should().ContainSingle().Which.Descripcion.Should().StartWith("Se abrió el correo");
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("200.21.70.10", true)]
    public void D7_SoloSeDescargaDeDireccionesPublicas(string ip, bool publica) =>
        DescargaFotoHttp.EsPublica(System.Net.IPAddress.Parse(ip)).Should().Be(publica);

    /// <summary>El HTML que quedó guardado de un correo del comparendo, por tipo.</summary>
    private async Task<(string Asunto, string Html, CanalCorreo? Canal)> CorreoAsync(Guid comparendo, TipoComunicacion tipo)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Flit.Comparendos.Api.Persistence.ComparendosDb>();
        var c = await db.Comunicaciones.AsNoTracking().SingleAsync(x => x.ComparendoId == comparendo && x.Tipo == tipo);
        var archivo = await db.Archivos.AsNoTracking().SingleAsync(a => a.Id == c.CuerpoHtmlArchivoId);
        var bytes = await scope.ServiceProvider.GetRequiredService<Flit.Comparendos.Api.Infraestructura.Archivos.IAlmacenArchivos>().LeerAsync(archivo.Referencia, CancellationToken.None);
        return (c.Asunto, System.Text.Encoding.UTF8.GetString(bytes!), c.CanalCorreo);
    }

    [Fact]
    public async Task HU13883_FLIT_DisenoDeTramites_YElDescuentoSegunLaVentana()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AVT-1");
        var id = ids["AVT1"];

        await SchedulerAsync(empresa);
        var (_, html1, canal) = await CorreoAsync(id, TipoComunicacion.Aviso1);
        canal.Should().Be(CanalCorreo.FlitSmtp);
        html1.Should().Contain("/assets/correo/flit-cabecera.png").And.Contain("AVISO DE COMPARENDO").And.Contain("50% de descuento")
            .And.Contain("Juan P&#233;rez").And.NotContain("Los plazos de descuento ya terminaron");

        api.Reloj.Hoy(new DateOnly(2026, 10, 20));
        await SchedulerAsync(empresa);
        var (asunto2, html2, _) = await CorreoAsync(id, TipoComunicacion.Aviso2);
        asunto2.Should().Be("Aún tienes 25% de descuento: comparendo AVT-1");
        html2.Should().Contain("El plazo del 50%").And.Contain("25% de descuento");
    }

    [Fact]
    public async Task HU13883_Renting_TextosDeV1_SuMarca_YNadaDeFlit()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AVR-1");
        var id = ids["AVR1"];
        // La empresa tiene «API Renting cliente» en Trámites (HU #13882).
        api.Empresas.Agregar(empresa, "Renting Colombia S.A.S", NitGestora, CanalCorreo.EmpresaApi);
        api.App.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove(CanalDeTramites.ClaveCache);

        await SchedulerAsync(empresa);
        var (asunto, html, canal) = await CorreoAsync(id, TipoComunicacion.Aviso1);
        canal.Should().Be(CanalCorreo.EmpresaApi);
        asunto.Should().Be("Información sobre la fotomulta N° AVR-1 asociada al vehículo con placa ABC124");
        html.Should().Contain("/assets/correo/renting-cabecera.jpg").And.Contain("/assets/correo/renting-pie.jpg")
            .And.Contain("Hemos identificado una fotomulta asociada a un vehículo rentado con <strong>Renting Colombia</strong>.")
            .And.Contain("Hoy puedes pagarla con el 50% de descuento")
            .And.Contain("El organismo de tránsito notificó la fotomulta el 1 de octubre de 2026", "ya hay notificación: no se dice que no la hay");
        html.Should().NotContain("FLIT").And.NotContain("flit", "el conductor de Renting no ve a FLIT");
    }

    [Theory]
    [InlineData("LAURA MARCELA GÓMEZ DE LA ROSA", "Laura Marcela Gómez de la Rosa")]
    [InlineData("RENTING COLOMBIA S.A.S.", "Renting Colombia S.A.S.")]
    [InlineData("Juan Pérez", "Juan Pérez")]
    [InlineData("CONDUCTOR DE PRUEBA QNQ836", "Conductor de Prueba QNQ836")]
    [InlineData("  ", "")]
    public void HU13883_NombresEnMayusculas_ANombrePropio(string entrada, string esperado) =>
        Flit.Comparendos.Api.Modulos.Plantillas.Formato.NombrePropio(entrada).Should().Be(esperado);

    /// <summary>Representante legal de la empresa que firma el cambio de contraventor, con firma y, si se pide, poder.</summary>
    private async Task RepresentanteAsync(Guid empresa, bool firma, bool poder)
    {
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.RepresentantesVer, Permisos.RepresentantesGestionar]));
        var rl = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@gestora.test", cargo = "Representante legal",
            permisoEnvioDp = true, permisoCambioContraventor = true,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var id = rl.GetProperty("id").GetGuid();
        async Task SubirAsync(string tipo, byte[] contenido, string contentType, string nombre)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent(contenido) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) } }, "archivo", nombre } };
            (await cliente.PostAsync($"/api/v1/comparendos/representantes/{id}/documentos/{tipo}", form)).EnsureSuccessStatusCode();
        }

        if (firma)
            await SubirAsync("firma", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="), "image/png", "firma.png");
        if (poder)
            await SubirAsync("escritura", System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%poder de prueba\n%%EOF"), "application/pdf", "poder.pdf");
    }

    private Task<List<string>> AdjuntosAsync(Guid comparendo, TipoComunicacion tipo) =>
        api.EnDbAsync(db => (from c in db.Comunicaciones
                             join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId
                             where c.ComparendoId == comparendo && c.Tipo == tipo
                             orderby a.Nombre
                             select a.Nombre).ToListAsync());

    [Fact]
    public async Task HU13884_ConRepresentante_AdjuntaLaAutorizacionFirmadaYElPoder()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AVA-1");
        await RepresentanteAsync(empresa, firma: true, poder: true);

        await SchedulerAsync(empresa);
        (await AdjuntosAsync(ids["AVA1"], TipoComunicacion.Aviso1)).Should().Equal("autorizacion-cambio-infractor-ABC124.pdf", "poder-gestora-de-prueba.pdf");
        var (_, html, _) = await CorreoAsync(ids["AVA1"], TipoComunicacion.Aviso1);
        html.Should().Contain("Adjuntamos la autorizaci").And.Contain("y el poder de la empresa");

        // El PDF de la autorización lleva el representante y el conductor.
        var pdf = await api.EnDbAsync(async db =>
        {
            var archivoId = await (from c in db.Comunicaciones join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId
                                   where c.ComparendoId == ids["AVA1"] && a.Nombre.StartsWith("autorizacion") select a.ArchivoId).SingleAsync();
            return await db.Archivos.SingleAsync(a => a.Id == archivoId);
        });
        pdf.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task HU13884_SinFirmaNiPoder_SaleSinEsosAdjuntos_YLaLineaDeTiempoLoDice()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, ids) = await PrepararAsync("AVS-1");
        await RepresentanteAsync(empresa, firma: false, poder: false);

        await SchedulerAsync(empresa);
        (await AdjuntosAsync(ids["AVS1"], TipoComunicacion.Aviso1)).Should().BeEmpty("nunca sale un adjunto vacío");
        var (_, html, _) = await CorreoAsync(ids["AVS1"], TipoComunicacion.Aviso1);
        html.Should().NotContain("Adjuntamos la autorizaci", "el texto no promete lo que no va");
        var evento = await api.EnDbAsync(db => db.Timeline.Where(e => e.ComparendoId == ids["AVS1"] && e.Tipo == TipoEventoTimeline.AvisoEnviado).Select(e => e.Descripcion).SingleAsync());
        evento.Should().Contain("Salió sin la autorización (el representante legal no tiene la firma cargada) ni el poder");
    }
}
