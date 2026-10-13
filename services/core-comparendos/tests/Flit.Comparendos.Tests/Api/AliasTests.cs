using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>§7.21 y RN-009 — textos de secretaría sin mapear y alias que los asocia con evento en el timeline.</summary>
public sealed class AliasTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    [Fact]
    public async Task SinMapear_YAlCrearAlias_SeAsignaElOtConEvento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("AL-1") with { Secretaria = "Insp. Tránsito Villa Inventada" })).Values.Single();
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).OrganismoTransitoId.Should().BeNull();

        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        var lista = await super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/organismos/sin-mapear", ct);
        var fila = lista.EnumerateArray().Single(f => f.GetProperty("variantes").EnumerateArray().Any(v => v.GetString() == "Insp. Tránsito Villa Inventada"));
        fila.GetProperty("activos").GetInt32().Should().Be(1);

        (await super.PostAsJsonAsync("/api/v1/comparendos/admin/organismos/alias",
            new { texto = "insp. transito villa inventada", organismoTransitoId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001") }, ct)).EnsureSuccessStatusCode();
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).OrganismoTransitoId.Should().Be(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001"));
        (await api.EnDbAsync(db => db.Timeline.AnyAsync(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.DatosActualizados && t.Descripcion.Contains("alias"), ct))).Should().BeTrue();
    }

    [Fact]
    public async Task F10_SinAlias_ElNumeroNacionalDiceElOt_SoloConCoincidenciaExacta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var ids = await IngresarAsync(api, empresa,
            Item("05001000000012345678") with { Secretaria = "Texto que ningún alias conoce" },
            Item("99999999000012345678") with { Secretaria = "Otro texto desconocido" },
            Item("0500100") with { Secretaria = "Texto corto desconocido" });
        var ot = await api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa).ToDictionaryAsync(c => c.NumeroNormalizado, c => c.OrganismoTransitoId, ct));
        ot["05001000000012345678"].Should().Be(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002"), "Medellín es 5001000 en el catálogo: perdió el cero inicial");
        ot["99999999000012345678"].Should().BeNull("ningún organismo tiene ese código");
        ot["0500100"].Should().BeNull("un número corto no es el formato nacional");
        ids.Should().HaveCount(3);
        (await api.EnDbAsync(db => db.OrganismosTransitoAlias.AnyAsync(a => a.TextoNormalizado == "TEXTO QUE NINGUN ALIAS CONOCE", ct))).Should().BeFalse("el número no crea alias del texto");
    }
}
