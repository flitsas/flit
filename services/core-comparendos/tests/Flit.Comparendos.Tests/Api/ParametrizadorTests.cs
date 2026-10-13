using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13473 — capacidades y correo por empresa.</summary>
public sealed class ParametrizadorTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private static object Capacidades(bool dp = true, bool cambio = false) => new
    {
        hasRightPetitionModule = dp, allowsContravenerConsultation = false, allowsContravenerChange = cambio, hasOcrAccess = true, hasReportesModule = false,
        tipoIntegracionConductor = "Manual", conjuntoPlantillas = "Flit", requiereRepresentanteLegal = true, empresaNombre = "Gestora Uno",
    };

    [Fact]
    public async Task AC_SuperAdminActivaCapacidades_YLaSesionLasRefleja()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = empresa, nombre = "Gestora Uno" }, ct)).EnsureSuccessStatusCode();
        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", Capacidades(), ct)).EnsureSuccessStatusCode();

        var sesion = await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer])).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/sesion", ct);
        sesion.GetProperty("empresa").GetProperty("capacidades").GetProperty("derechosPeticion").GetBoolean().Should().BeTrue();
        sesion.GetProperty("empresa").GetProperty("capacidades").GetProperty("reportes").GetBoolean().Should().BeFalse();

        var lista = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/empresas", ct);
        lista.EnumerateArray().Should().Contain(e => e.GetProperty("id").GetGuid() == empresa);
        (await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/admin/empresas/{empresa}/auditoria", ct)).GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task NitDeLaEmpresa_SeValidaYSeMuestraConDv()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = empresa, nombre = "Gestora NIT" }, ct)).EnsureSuccessStatusCode();
        var url = $"/api/v1/comparendos/admin/empresas/{empresa}/capacidades";
        object Con(string nit) => new
        {
            hasRightPetitionModule = true, allowsContravenerConsultation = false, allowsContravenerChange = false, hasOcrAccess = false, hasReportesModule = false,
            tipoIntegracionConductor = "Manual", conjuntoPlantillas = "Flit", requiereRepresentanteLegal = true, empresaNombre = "Gestora NIT", empresaNit = nit,
        };

        (await Super.PutAsJsonAsync(url, Con("900123456-1"), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "el DV de 900123456 es 8");
        var r = await Super.PutAsJsonAsync(url, Con("900.123.456"), ct);
        r.EnsureSuccessStatusCode();
        (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("empresaNit").GetString().Should().Be("900123456-8");
    }

    [Fact]
    public async Task AC2_AdminDeGestora_NoActivaCapacidades_PeroEditaSuCorreo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var admin = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar]));

        (await admin.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", Capacidades(), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var r = await admin.PutAsJsonAsync("/api/v1/comparendos/configuracion/correo", new
        {
            canalCorreo = "FlitSmtp", remitenteNombre = "Renting Colombia", correoRespuesta = "juridico@renting.test",
            cc = new[] { new { correo = "copia@renting.test", tipos = new[] { "DpOt" } } }, bcc = Array.Empty<object>(),
            destinatariosReportes = new[] { "gerencia@renting.test" }, correosAlertaOperativa = Array.Empty<string>(),
        }, ct);
        r.EnsureSuccessStatusCode();
        var config = await admin.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/configuracion", ct);
        config.GetProperty("remitenteNombre").GetString().Should().Be("Renting Colombia");
        config.GetProperty("cc")[0].GetProperty("tipos")[0].GetString().Should().Be("DpOt");
        config.GetProperty("hasRightPetitionModule").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ActivarCambioDeContraventor_PasaLosActivosASinSolicitar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        await Super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = empresa, nombre = "Gestora Dos" }, ct);
        await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "PAR123" } }, ct);
        api.Fuente.Respuestas["SimitPlaca:PAR123"] = () => RespuestaFuente.Exitosa([new ItemFuente { Numero = "P-1", Placa = "PAR123", FechaNotificacion = new DateOnly(2026, 10, 1) }]);
        await api.CicloAsync(CanalIngesta.SimitPlaca, empresa);
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.TenantId == empresa, ct))).EstadoCambioContraventor.Should().Be(EstadoCambioContraventor.NoAplica);

        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", Capacidades(cambio: true), ct)).EnsureSuccessStatusCode();

        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.TenantId == empresa, ct))).EstadoCambioContraventor.Should().Be(EstadoCambioContraventor.SinSolicitar);
    }

    [Fact]
    public async Task CorreoInvalido_SeRechaza()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var admin = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.ConfiguracionEditar]));
        var r = await admin.PutAsJsonAsync("/api/v1/comparendos/configuracion/correo", new { canalCorreo = "FlitSmtp", correoRespuesta = "no-es-correo" }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
