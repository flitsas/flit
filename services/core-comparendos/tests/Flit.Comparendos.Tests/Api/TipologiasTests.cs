using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13474 — catálogo de tipologías y habilitación por empresa.</summary>
public sealed class TipologiasTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    [Fact]
    public async Task AC1_Semilla_ConLasOnceTesisDeV1()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var catalogo = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/tipologias", TestContext.Current.CancellationToken);
        // Las once tesis sembradas de V1, por código: el fixture es compartido y otras pruebas crean tipologías propias.
        catalogo.EnumerateArray().Select(t => t.GetProperty("codigo").GetString())
            .Should().Contain(["AA", "AN", "DA", "DBN", "DC", "DCI", "DI", "DLL", "DP", "DPR", "DRP"]);
        var dpr = catalogo.EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == "DPR");
        dpr.GetProperty("estadosLegalesPermitidos").EnumerateArray().Select(e => e.GetString()).Should().Equal("Prescrito");
        dpr.GetProperty("plazoRespuestaOtDiasHabiles").GetInt32().Should().Be(15);
    }

    [Fact]
    public async Task AC2_HabilitarParaUnaEmpresa_NoAfectaAOtra()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var dpr = (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/tipologias", ct)).EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == "DPR");

        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{a}/tipologias/{dpr.GetProperty("id").GetGuid()}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();

        var deA = await api.Cliente(new UsuarioDePrueba(a, [])).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/tipologias?soloHabilitadas=true", ct);
        var deB = await api.Cliente(new UsuarioDePrueba(b, [])).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/tipologias?soloHabilitadas=true", ct);
        deA.EnumerateArray().Select(t => t.GetProperty("codigo").GetString()).Should().Equal("DPR");
        deB.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CrearYEditar_ValidaEstadosYPrefijo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var malo = await Super.PostAsJsonAsync("/api/v1/comparendos/admin/tipologias", new
        {
            codigo = "DP-DUP", nombre = "Doble sanción", descripcionJuridica = "x", prefijoRadicado = "DUP1", estadosLegalesPermitidos = Array.Empty<string>(), permiteMasivo = false, plazoRespuestaOtDiasHabiles = 15,
        }, ct);
        malo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var r = await Super.PostAsJsonAsync("/api/v1/comparendos/admin/tipologias", new
        {
            codigo = "dp-dup", nombre = "Doble sanción", descripcionJuridica = "El mismo hecho se sancionó dos veces.", prefijoRadicado = "dup",
            estadosLegalesPermitidos = new[] { "EnTiempo", "Vencido" }, permiteMasivo = false, plazoRespuestaOtDiasHabiles = 15, adjuntosRequeridos = new[] { "Cedula" },
        }, ct);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var creada = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        creada.GetProperty("codigo").GetString().Should().Be("DP-DUP");
        creada.GetProperty("prefijoRadicado").GetString().Should().Be("DUP");

        (await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.ConfiguracionEditar])).PostAsJsonAsync("/api/v1/comparendos/admin/tipologias", new { codigo = "X" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Epic #12977: desactivar una tesis del catálogo respeta a las empresas que ya la tenían; deja de ofrecerse a las demás.</summary>
    [Fact]
    public async Task Desactivar_RespetaALasQueYaLaTenian()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var r = await Super.PostAsJsonAsync("/api/v1/comparendos/admin/tipologias", new
        {
            codigo = "dp-ret", nombre = "Tesis que se retira", descripcionJuridica = "Fundamento de prueba.", prefijoRadicado = "RET",
            estadosLegalesPermitidos = new[] { "EnTiempo" }, permiteMasivo = false, plazoRespuestaOtDiasHabiles = 15,
        }, ct);
        var t = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = t.GetProperty("id").GetGuid();
        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{a}/tipologias/{id}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();

        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/tipologias/{id}", new
        {
            codigo = "DP-RET", nombre = "Tesis que se retira", descripcionJuridica = "Fundamento de prueba.", prefijoRadicado = "RET",
            estadosLegalesPermitidos = new[] { "EnTiempo" }, permiteMasivo = false, plazoRespuestaOtDiasHabiles = 15, activo = false,
        }, ct)).EnsureSuccessStatusCode();

        var deA = await api.Cliente(new UsuarioDePrueba(a, [])).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/tipologias?soloHabilitadas=true", ct);
        deA.EnumerateArray().Select(x => x.GetProperty("codigo").GetString()).Should().Contain("DP-RET", "la empresa que la tenía la conserva");
        var nueva = await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{b}/tipologias/{id}", new { habilitada = true }, ct);
        nueva.StatusCode.Should().Be(HttpStatusCode.Conflict, "ya no se habilita a más empresas");
    }
}
