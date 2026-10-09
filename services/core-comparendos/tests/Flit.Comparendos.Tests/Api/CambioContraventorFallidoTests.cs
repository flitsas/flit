using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>La plataforma rechaza de forma definitiva todo correo a una bandeja de radicación de OT.</summary>
public sealed class EnvioQueRechazaOt : IEnvioCorreo
{
    public Task<ResultadoEnvioCorreo> EnviarAsync(Guid? tenantId, CorreoParaEnviar correo, CancellationToken ct) =>
        Task.FromResult(correo.DestinatarioEmail.StartsWith("radicacion@", StringComparison.Ordinal)
            ? new ResultadoEnvioCorreo("RECHAZADO", "Buzón inexistente", false, null)
            : new ResultadoEnvioCorreo(ResultadoEnvioCorreo.Enviado, null, false, Guid.NewGuid().ToString()));
}

public sealed class ApiConOtCaido : ApiConFuente
{
    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
        services.AddScoped<IEnvioCorreo, EnvioQueRechazaOt>();
    }
}

/// <summary>§6.10 pasos 3-4: si el correo al OT no sale, no hay solicitud ni aviso 4 al conductor.</summary>
public sealed class CambioContraventorFallidoTests(ApiConOtCaido api) : IClassFixture<ApiConOtCaido>
{
    [Fact]
    public async Task CorreoAlOtFallido_NoEnviaAviso4_YVuelveASinSolicitar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, cambio: true);
        var id = (await IngresarAsync(api, empresa, Item("CCF-1"))).Values.Single();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa,
            [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.ContraventorDocumentos, Permisos.CambioContraventorSolicitar, Permisos.RepresentantesGestionar]));
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@ejemplo.com", declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        foreach (var tipo in new[] { "Cedula", "Licencia" })
        {
            using var pdf = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", "d.pdf" } };
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/documentos?tipo={tipo}", pdf, ct)).EnsureSuccessStatusCode();
        }

        (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", cargo = "Representante legal",
            permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).EnsureSuccessStatusCode();

        var r = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        r.GetProperty("correoOt").GetString().Should().Be("Fallido");
        (await api.EnDbAsync(db => db.AvisosConductor.AnyAsync(a => a.ComparendoId == id && a.TipoAviso == TipoAviso.Aviso4, ct))).Should().BeFalse();
        (await api.EnDbAsync(db => db.Comunicaciones.AnyAsync(c => c.ComparendoId == id && c.Tipo == TipoComunicacion.Aviso4, ct))).Should().BeFalse();
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).EstadoCambioContraventor.Should().Be(EstadoCambioContraventor.SinSolicitar);
        (await api.EnDbAsync(db => db.SolicitudesCambio.SingleAsync(s => s.ComparendoId == id, ct))).Estado.Should().Be(EstadoSolicitudCambio.EnvioFallido);
    }
}
