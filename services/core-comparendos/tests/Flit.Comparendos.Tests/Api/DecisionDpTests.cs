using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// HU #14608 conectada a datos reales: <c>GET /comparendos/{id}/dp/decision</c> arma los datos del comparendo, las
/// tipologías disponibles de la empresa y devuelve qué se podría alegar y cuál se elegiría, sin enviar nada.
/// </summary>
public sealed class DecisionDpTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string Base = "/api/v1/comparendos";
    private static readonly DateOnly Hoy = new(2026, 11, 18);

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private HttpClient Operador(Guid empresa) => api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer, Permisos.DpGenerar]));

    private async Task<Guid> TipologiaAsync(string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/tipologias", TestContext.Current.CancellationToken))
        .EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    private async Task HabilitarAsync(Guid empresa, string codigo) =>
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{await TipologiaAsync(codigo)}", new { habilitada = true }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    /// <summary>Comparendo de agente (no fotomulta) de una persona jurídica, impuesto y notificado hace más de un año.</summary>
    private static ItemFuente Viejo(string numero) => Item(numero, fotomulta: false) with
    {
        FechaImposicion = new DateOnly(2025, 6, 1), FechaNotificacion = new DateOnly(2025, 6, 5), CodigoInfraccion = "C29",
    };

    private async Task<(Guid Empresa, Guid Comparendo)> EmpresaConComparendoAsync(ItemFuente item, params string[] habilitadas)
    {
        api.Reloj.Hoy(Hoy);
        var empresa = await EmpresaAsync(api, dp: true);
        foreach (var t in habilitadas)
            await HabilitarAsync(empresa, t);
        var id = (await IngresarAsync(api, empresa, item)).Values.Single();
        return (empresa, id);
    }

    private async Task<JsonElement> DecisionAsync(Guid empresa, Guid comparendo, bool automatico = false) =>
        await Operador(empresa).GetFromJsonAsync<JsonElement>($"{Base}/comparendos/{comparendo}/dp/decision{(automatico ? "?automatico=true" : string.Empty)}", TestContext.Current.CancellationToken);

    [Fact]
    public async Task Un_comparendo_viejo_se_decide_como_caducidad_y_lista_lo_que_tambien_aplica()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, id) = await EmpresaConComparendoAsync(Viejo("DEC-1"), "DCI");

        var d = await DecisionAsync(empresa, id);

        d.GetProperty("elegible").GetBoolean().Should().BeTrue();
        d.GetProperty("elegida").GetString().Should().Be("DCI");
        d.GetProperty("tipologiaIdElegida").GetGuid().Should().Be(await TipologiaAsync("DCI"));
        var aplicables = d.GetProperty("aplicables").EnumerateArray().ToList();
        aplicables.Select(a => a.GetProperty("codigo").GetString()).Should().Equal("DCI", "DLL");
        aplicables[0].GetProperty("disponible").GetBoolean().Should().BeTrue();
        // DLL aplica por regla pero la empresa no la tiene habilitada, así que se muestra y no se elige.
        aplicables[1].GetProperty("disponible").GetBoolean().Should().BeFalse();
        aplicables.Should().OnlyContain(a => a.GetProperty("razon").GetString()!.Length > 0 && a.GetProperty("nombre").GetString()!.Length > 0);
    }

    [Fact]
    public async Task Una_tipologia_que_no_procede_en_el_estado_legal_de_hoy_no_se_elige()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        // El comparendo está «Caducado»; DLL solo procede en tiempo o vencido, aunque la regla de V1 diga que aplica.
        var (empresa, id) = await EmpresaConComparendoAsync(Viejo("DEC-2"), "DLL");

        var d = await DecisionAsync(empresa, id);

        d.GetProperty("elegible").GetBoolean().Should().BeFalse();
        d.GetProperty("motivo").GetString().Should().Be("SinTipologiaDisponible");
        d.GetProperty("aplicables").EnumerateArray().Select(a => a.GetProperty("codigo").GetString()).Should().Contain("DLL");
    }

    [Fact]
    public async Task Como_lo_haria_el_automatico_exige_automatizable_y_revision_juridica()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, id) = await EmpresaConComparendoAsync(Viejo("DEC-3"), "DCI");

        // Habilitada pero sin marca ni revisión: una persona la podría enviar, el automático no.
        (await DecisionAsync(empresa, id)).GetProperty("elegida").GetString().Should().Be("DCI");
        var sinMarca = await DecisionAsync(empresa, id, automatico: true);
        sinMarca.GetProperty("elegible").GetBoolean().Should().BeFalse();
        sinMarca.GetProperty("motivo").GetString().Should().Be("SinTipologiaDisponible");

        // Con la versión publicada aprobada y la marca encendida, sí.
        var plantillas = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas?conjunto=Flit&tipo=DpDocumento", ct);
        var dci = await TipologiaAsync("DCI");
        var version = plantillas.EnumerateArray().Single(p => p.GetProperty("tipologiaId").GetGuid() == dci).GetProperty("publicada").GetProperty("id").GetGuid();
        (await Super.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota = "Aprobado" }, ct)).EnsureSuccessStatusCode();
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{dci}/automatizable", new { automatizable = true }, ct)).EnsureSuccessStatusCode();

        (await DecisionAsync(empresa, id, automatico: true)).GetProperty("elegida").GetString().Should().Be("DCI");
    }

    [Fact]
    public async Task Un_comparendo_pagado_no_es_elegible_pero_muestra_lo_que_se_podria_alegar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, id) = await EmpresaConComparendoAsync(Viejo("DEC-4"), "DCI");
        await api.EnDbAsync(async db =>
        {
            (await db.Comparendos.SingleAsync(c => c.Id == id)).Pagado = true;
            return await db.SaveChangesAsync();
        });

        var d = await DecisionAsync(empresa, id);

        d.GetProperty("elegible").GetBoolean().Should().BeFalse();
        d.GetProperty("motivo").GetString().Should().Be("Pagado");
        d.GetProperty("aplicables").EnumerateArray().Select(a => a.GetProperty("codigo").GetString()).Should().Contain("DCI");
    }

    [Fact]
    public async Task Una_fotomulta_recien_notificada_espera_por_el_descuento_del_cincuenta_por_ciento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var reciente = Item("DEC-5", fotomulta: true) with { FechaImposicion = Hoy.AddDays(-3), FechaNotificacion = Hoy.AddDays(-1) };
        var (empresa, id) = await EmpresaConComparendoAsync(reciente, "DLL");

        var d = await DecisionAsync(empresa, id);

        d.GetProperty("elegible").GetBoolean().Should().BeFalse();
        d.GetProperty("motivo").GetString().Should().Be("VentanaDescuento");
    }

    [Fact]
    public async Task Sin_el_permiso_se_rechaza_y_otra_empresa_no_ve_el_comparendo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, id) = await EmpresaConComparendoAsync(Viejo("DEC-6"), "DCI");
        var sinPermiso = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer]));
        var otra = await EmpresaAsync(api, dp: true);

        (await sinPermiso.GetAsync($"{Base}/comparendos/{id}/dp/decision", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Operador(otra).GetAsync($"{Base}/comparendos/{id}/dp/decision", ct)).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }
}
