using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13479 — solicitar el cambio de contraventor ante el OT.</summary>
public sealed class CambioContraventorTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador =
    [
        Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.ContraventorDocumentos, Permisos.CambioContraventorSolicitar,
        Permisos.CambioContraventorResponder, Permisos.RepresentantesGestionar, Permisos.AvisosEnviar,
    ];

    private static MultipartFormDataContent Pdf(string nombre) =>
        new() { { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", nombre } };

    private async Task<(Guid Empresa, Guid Id, HttpClient Cliente)> PrepararAsync(bool conDocumentos = true)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var ct = TestContext.Current.CancellationToken;
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, cambio: true);
        var id = (await IngresarAsync(api, empresa, propietarioEsElNit: true, Item($"CC-{Random.Shared.Next(100000)}"))).Values.Single();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@example.test", declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        if (conDocumentos)
        {
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/documentos?tipo=Cedula", Pdf("cedula.pdf"), ct)).EnsureSuccessStatusCode();
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/documentos?tipo=Licencia", Pdf("licencia.pdf"), ct)).EnsureSuccessStatusCode();
        }

        await RepresentanteDelNitAsync(api, cliente, empresa, ct);
        return (empresa, id, cliente);
    }

    private static async Task<HttpResponseMessage> Responder(HttpClient cliente, Guid id, Guid solicitud, string resultado, string causal) =>
        await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/{solicitud}/respuesta",
            new MultipartFormDataContent { { new StringContent(resultado), "resultado" }, { new StringContent(causal), "causal" }, { new StringContent("2026-10-09"), "fecha" } });

    [Fact]
    public async Task AC1_FlujoCompleto_SolicitudRechazoNuevaSolicitudYAceptacion()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, id, cliente) = await PrepararAsync();

        var viabilidad = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/viabilidad", ct);
        viabilidad.GetProperty("viable").GetBoolean().Should().BeTrue();
        foreach (var url in new[] { "vista-previa", "vista-previa?documento=carta" })
        {
            var pdf = await cliente.GetAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/{url}", ct);
            pdf.StatusCode.Should().Be(HttpStatusCode.OK, url);
            pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf", "§7.4: el formato y la carta al conductor se revisan antes de solicitar");
        }

        var s1 = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        s1.GetProperty("correoOt").GetString().Should().Be("Enviado");
        var correoOt = await api.EnDbAsync(db => db.Comunicaciones.SingleAsync(c => c.ComparendoId == id && c.Tipo == TipoComunicacion.CambioContraventorOt, ct));
        (await api.EnDbAsync(db => db.ComunicacionAdjuntos.CountAsync(a => a.ComunicacionId == correoOt.Id, ct))).Should().Be(3, "formato, cédula y licencia");
        (await api.EnDbAsync(db => db.AvisosConductor.AnyAsync(a => a.ComparendoId == id && a.TipoAviso == TipoAviso.Aviso4, ct))).Should().BeTrue();

        (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var sin = await Responder(cliente, id, s1.GetProperty("id").GetGuid(), "Rechazado", "corta");
        sin.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Responder(cliente, id, s1.GetProperty("id").GetGuid(), "Rechazado", "Falta la firma del conductor en el formato")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).EstadoCambioContraventor.Should().Be(EstadoCambioContraventor.RechazadoOt);

        var s2 = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        s2.GetProperty("numeroIntento").GetInt32().Should().Be(2);
        (await Responder(cliente, id, s2.GetProperty("id").GetGuid(), "Aceptado", string.Empty)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).EstadoCambioContraventor.Should().Be(EstadoCambioContraventor.AceptadoOt);
    }

    [Fact]
    public async Task SinCedulaNiLicencia_NoEsViable()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, id, cliente) = await PrepararAsync(conDocumentos: false);
        var viabilidad = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/viabilidad", ct);
        viabilidad.GetProperty("viable").GetBoolean().Should().BeFalse();
        viabilidad.GetProperty("condiciones").EnumerateArray().Where(c => !c.GetProperty("cumple").GetBoolean()).Select(c => c.GetProperty("codigo").GetString()).Should().Equal("CEDULA", "LICENCIA");
        var r = await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct);
        (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("CAMBIO_CEDULA");
    }

    /// <summary>Hallazgo B9-1: el propietario según la fuente no es la empresa ni uno de sus NITs → no se pide a su nombre.</summary>
    [Fact]
    public async Task PropietarioAjenoALaEmpresa_NoEsViable()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, id, cliente) = await PrepararAsync();
        await api.EnDbAsync(async db =>
        {
            var c = await db.Comparendos.SingleAsync(x => x.Id == id, ct);
            c.NumeroDocumentoInfractor = "900123456";
            await db.SaveChangesAsync(ct);
        });

        var viabilidad = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/viabilidad", ct);
        viabilidad.GetProperty("condiciones").EnumerateArray().Where(c => !c.GetProperty("cumple").GetBoolean()).Select(c => c.GetProperty("codigo").GetString())
            .Should().Equal("PROPIETARIO", "REPRESENTANTE");
        _ = empresa;
    }

    /// <summary>
    /// Hallazgo B9-1: el RL de la empresa no firma por otro NIT (el formato diría que representa a ese propietario). El NIT
    /// con el dígito pegado (como lo trae la fuente) cuenta como el mismo propietario.
    /// </summary>
    [Fact]
    public async Task NitVigiladoSinRepresentantePropio_ElDeLaEmpresaNoFirma()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, id, cliente) = await PrepararAsync();
        await api.EnDbAsync(async db =>
        {
            var nit = await db.IdentificacionesMonitoreadas.SingleAsync(n => n.TenantId == empresa, ct);
            var c = await db.Comparendos.SingleAsync(x => x.Id == id, ct);
            c.NumeroDocumentoInfractor = nit.NumeroBase + Flit.Comparendos.Domain.Identificadores.Nit.DigitoVerificacion(nit.NumeroBase);
            await db.RepresentantesLegales.Where(r => r.TenantId == empresa).ExecuteUpdateAsync(s => s.SetProperty(r => r.Activo, false), ct);
            await db.SaveChangesAsync(ct);
        });
        (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52999999", nombres = "RL de la empresa", cargo = "Representante legal",
            permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).EnsureSuccessStatusCode();

        var viabilidad = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/viabilidad", ct);
        viabilidad.GetProperty("condiciones").EnumerateArray().Where(c => !c.GetProperty("cumple").GetBoolean()).Select(c => c.GetProperty("codigo").GetString())
            .Should().Equal(["REPRESENTANTE"], "el propietario (con DV pegado) es el NIT vigilado, pero solo el RL de ese NIT puede firmar por él");
    }
}
