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
        tipoIntegracionConductor = "Manual", conjuntoPlantillas = "Flit", requiereRepresentanteLegal = true,
    };

    [Fact]
    public async Task AC_SuperAdminActivaCapacidades_YLaSesionLasRefleja()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        (await api.RegistrarEmpresaAsync(Super, empresa, "Gestora Uno", ct: ct)).EnsureSuccessStatusCode();
        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", Capacidades(), ct)).EnsureSuccessStatusCode();

        var sesion = await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer])).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/sesion", ct);
        sesion.GetProperty("empresa").GetProperty("capacidades").GetProperty("derechosPeticion").GetBoolean().Should().BeTrue();
        sesion.GetProperty("empresa").GetProperty("capacidades").GetProperty("reportes").GetBoolean().Should().BeFalse();

        var lista = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/empresas", ct);
        var fila = lista.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == empresa);
        fila.GetProperty("estadoConfiguracion").GetString().Should().Be("Pendiente", "naranja hasta que la empresa pueda operar (Epic GDC-3)");
        fila.GetProperty("faltantes").EnumerateArray().Select(x => x.GetString()).Should()
            .Contain(["Sin NIT ni placa monitoreados", "Consultas sin activar", "Sin representante legal", "Sin tipologías de DP habilitadas"]);
        (await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/admin/empresas/{empresa}/auditoria", ct)).GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RegistrarEmpresa_SeEligeDeLaPlataforma_YTraeNombreYNitConDv()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        api.Empresas.Agregar(empresa, "Renting Colombia S.A.S", "890903938");

        var lista = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/empresas/plataforma", ct);
        var fila = lista.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == empresa);
        fila.GetProperty("nit").GetString().Should().Be("890903938-8", "el dígito se calcula (DIAN, módulo 11)");
        fila.GetProperty("registrada").GetBoolean().Should().BeFalse();

        var r = await Super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = empresa }, ct);
        r.EnsureSuccessStatusCode();
        var config = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        config.GetProperty("empresaNombre").GetString().Should().Be("Renting Colombia S.A.S");
        config.GetProperty("empresaNit").GetString().Should().Be("890903938-8");
        (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/empresas/plataforma", ct)).EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == empresa).GetProperty("registrada").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RegistrarEmpresa_SinElProductoEnLaPlataforma_SeRechaza()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = Guid.NewGuid() }, ct)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "solo se registran empresas con Comparendos encendido");
    }

    [Fact]
    public async Task AbrirEmpresa_PoneAlDiaNombreYNitDesdeLaPlataforma()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        (await api.RegistrarEmpresaAsync(Super, empresa, "Nombre viejo", "900123456", ct)).EnsureSuccessStatusCode();
        api.Empresas.Agregar(empresa, "Nombre nuevo S.A.S", "800197268");

        var config = await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/admin/empresas/{empresa}/configuracion", ct);
        config.GetProperty("empresaNombre").GetString().Should().Be("Nombre nuevo S.A.S");
        config.GetProperty("empresaNit").GetString().Should().Be("800197268-4");
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
        await api.RegistrarEmpresaAsync(Super, empresa, "Gestora Dos", ct: ct);
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
