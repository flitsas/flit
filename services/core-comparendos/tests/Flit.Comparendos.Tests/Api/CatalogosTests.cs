using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Textos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13459 — catálogo de OT, alias y municipios.</summary>
public sealed class CatalogosTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    [Fact]
    public async Task AC1_CatalogoSembrado_ConCodigoYMunicipio()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var pagina = await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.BandejaVer]))
            .GetFromJsonAsync<JsonElement>("/api/v1/comparendos/organismos?buscar=bogota", TestContext.Current.CancellationToken);

        pagina.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        pagina.GetProperty("items").EnumerateArray().Should().Contain(o => o.GetProperty("codigo").GetString() == "11001000");
    }

    [Fact]
    public async Task AC2_Alias_AsociaElTextoDeLaFuenteAlOt()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var bogota = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

        var r = await Super.PostAsJsonAsync("/api/v1/comparendos/admin/organismos/alias", new { texto = "Stría  Ttoyttte Bogotá", organismoTransitoId = bogota }, ct);
        r.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var scope = api.App.Services.CreateAsyncScope();
        var mapeador = scope.ServiceProvider.GetRequiredService<MapeadorOt>();
        (await mapeador.MapearAsync("STRIA TTOYTTTE BOGOTA", ct)).Should().Be(bogota);
    }

    [Fact]
    public async Task Mapeo_PorNombreExacto_CreaAliasAutomatico()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var scope = api.App.Services.CreateAsyncScope();
        var mapeador = scope.ServiceProvider.GetRequiredService<MapeadorOt>();

        var id = await mapeador.MapearAsync("Secretaría Distrital de Movilidad de Bogotá", ct);

        id.Should().Be(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001"));
        await scope.ServiceProvider.GetRequiredService<Flit.Comparendos.Api.Persistence.ComparendosDb>().SaveChangesAsync(ct);
        (await api.EnDbAsync(db => db.OrganismosTransitoAlias.AnyAsync(a => a.Origen == "AUTOMATICO" && a.TextoNormalizado == TextoNormalizado.De("Secretaría Distrital de Movilidad de Bogotá"), ct)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CorreosDeRadicacion_SoloSuperAdminYValidados()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002");

        var operador = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.OtsGestionar]));
        (await operador.PatchAsJsonAsync($"/api/v1/comparendos/admin/organismos/{id}", new { correosRadicacion = new[] { "x@ot.gov.co" } }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Super.PatchAsJsonAsync($"/api/v1/comparendos/admin/organismos/{id}", new { correosRadicacion = new[] { "no-es-correo" } }, ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var ok = await Super.PatchAsJsonAsync($"/api/v1/comparendos/admin/organismos/{id}", new { correosRadicacion = new[] { " Notificaciones@Medellin.gov.co ", "notificaciones@medellin.gov.co" } }, ct);
        (await ok.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("correosRadicacion").EnumerateArray().Select(c => c.GetString())
            .Should().Equal("notificaciones@medellin.gov.co");
    }

    [Fact]
    public async Task Municipios_SeCreanYDesactivan()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var creado = await (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/municipios", new { codigoFuente = "CALI", nombre = "Cali" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        var cambio = await Super.PatchAsJsonAsync($"/api/v1/comparendos/admin/municipios/{creado.GetProperty("id").GetGuid()}", new { activo = false }, ct);

        (await cambio.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("activo").GetBoolean().Should().BeFalse();
        (await Super.PostAsJsonAsync("/api/v1/comparendos/admin/municipios", new { codigoFuente = "CALI", nombre = "Otra" }, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
