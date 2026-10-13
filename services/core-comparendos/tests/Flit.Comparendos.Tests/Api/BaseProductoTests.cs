using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13454 y #13456 — el servicio aplica permisos y empresa del token, y siembra calendario y catálogos.</summary>
public sealed class BaseProductoTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task HU13454_Sesion_DevuelvePermisosYCapacidadesDeLaEmpresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var usuario = new UsuarioDePrueba(Empresa, [Permisos.BandejaVer, Permisos.DpEnviar]);

        var sesion = await api.Cliente(usuario).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/sesion", TestContext.Current.CancellationToken);

        sesion.GetProperty("permisos").EnumerateArray().Select(p => p.GetString()).Should().BeEquivalentTo(Permisos.BandejaVer, Permisos.DpEnviar);
        sesion.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(Empresa);
        sesion.GetProperty("empresa").GetProperty("capacidades").GetProperty("derechosPeticion").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task HU13454_SinToken_Responde401()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var respuesta = await api.Anonimo().GetAsync("/api/v1/comparendos/sesion", TestContext.Current.CancellationToken);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HU13454_SuperAdmin_TieneTodosLosPermisosYNingunaEmpresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sesion = await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true))
            .GetFromJsonAsync<JsonElement>("/api/v1/comparendos/sesion", TestContext.Current.CancellationToken);

        sesion.GetProperty("usuario").GetProperty("esSuperAdmin").GetBoolean().Should().BeTrue();
        sesion.GetProperty("permisos").GetArrayLength().Should().Be(Permisos.Modulos.Sum(m => m.Permisos.Length));
        sesion.GetProperty("empresa").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task HU13456_Semillas_CalendarioOrganismosTrabajosYTipologias()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (dias, festivos2026, organismos, trabajos, tipologias) = await api.EnDbAsync(async db => (
            await db.CalendarioDias.CountAsync(ct),
            await db.CalendarioDias.CountAsync(d => d.EsFestivo && d.Fecha.Year == 2026, ct),
            await db.OrganismosTransito.CountAsync(ct),
            await db.TrabajosProgramados.CountAsync(ct),
            await db.Tipologias.CountAsync(ct)));

        dias.Should().Be(new DateOnly(2040, 12, 31).DayNumber - new DateOnly(2015, 1, 1).DayNumber + 1);
        festivos2026.Should().Be(18);
        organismos.Should().Be(298);
        trabajos.Should().Be(5, "avisos, contraventor, reportes semanal y mensual, y la réplica de organismos; la ingesta no tiene cron global: cada empresa tiene su agenda (HU #13521)");
        // Correos de radicación importados de V1 (traffic_secretaries.notifier_email): sin ellos no sale ningún DP.
        var conCorreo = await api.EnDbAsync(db => db.OrganismosTransito.CountAsync(o => o.CorreosRadicacion.Count > 0));
        conCorreo.Should().Be(267);
        (await api.EnDbAsync(db => db.OrganismosTransito.SingleAsync(o => o.Codigo == "11001000"))).CorreosRadicacion.Should().Equal("judicial@movilidadbogota.gov.co");
        tipologias.Should().Be(11);
    }

    [Fact]
    public void HU13454_Manifiesto_SlugsUnicosConPrefijoYRolesConocidos()
    {
        var slugs = Permisos.Modulos.SelectMany(m => m.Permisos).Select(p => p.Slug).ToList();
        slugs.Should().OnlyHaveUniqueItems().And.OnlyContain(s => s.StartsWith("comparendos.", StringComparison.Ordinal));
        Permisos.RolesPorDefecto.SelectMany(r => r.Permisos).Should().BeSubsetOf(slugs);
        Permisos.RolesPorDefecto.Select(r => r.Codigo).Should().Contain("admin_comparendos");
    }
}
