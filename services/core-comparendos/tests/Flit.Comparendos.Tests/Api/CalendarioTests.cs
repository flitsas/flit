using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>§7.25 y RN-075 — festivos manuales y recálculo de plazos.</summary>
public sealed class CalendarioTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    [Fact]
    public async Task FestivoManual_AvisaImpacto_RecalculaYSePuedeQuitar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("CAL-1"))).Values.Single();
        var antes = await api.EnDbAsync(db => db.Comparendos.AsNoTracking().SingleAsync(c => c.Id == id, ct));
        antes.FechaLimiteGestion.Should().Be(new DateOnly(2026, 10, 19));

        var anio = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/calendario?anio=2026", ct);
        anio.GetProperty("dias").GetArrayLength().Should().Be(365);
        anio.GetProperty("dias").EnumerateArray().Single(d => d.GetProperty("fecha").GetString() == "2026-10-12").GetProperty("esFestivo").GetBoolean().Should().BeTrue();

        (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/calendario/impacto?fecha=2026-10-14", ct)).GetProperty("comparendos").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/calendario/2026-10-10", new { esFestivo = true, descripcion = "x" }, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "sábado");
        (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/calendario/2026-10-14", new { esFestivo = true }, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "sin descripción");
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer])).PutAsJsonAsync("/api/v1/comparendos/admin/calendario/2026-10-14", new { esFestivo = true, descripcion = "Paro" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var r = await (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/calendario/2026-10-14", new { esFestivo = true, descripcion = "Día cívico" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        r.GetProperty("recalculados").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var despues = await api.EnDbAsync(db => db.Comparendos.AsNoTracking().SingleAsync(c => c.Id == id, ct));
        despues.FechaLimiteGestion.Should().Be(new DateOnly(2026, 10, 20), "el 14 dejó de ser hábil");
        var evento = await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.PlazosCalculados).OrderByDescending(t => t.OcurridoEn).FirstAsync(ct));
        evento.Descripcion.Should().Contain("Día cívico");
        evento.Datos.Should().Contain("2026-10-19").And.Contain("2026-10-20");
        (await api.EnDbAsync(db => db.AuditoriasConfiguracion.AnyAsync(a => a.Entidad == "calendario_dia" && a.EntidadId == "2026-10-14", ct))).Should().BeTrue();

        (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/calendario/2026-10-14", new { esFestivo = false }, ct)).EnsureSuccessStatusCode();
        (await api.EnDbAsync(db => db.Comparendos.AsNoTracking().SingleAsync(c => c.Id == id, ct))).FechaLimiteGestion.Should().Be(new DateOnly(2026, 10, 19));
        var dia = await api.EnDbAsync(db => db.CalendarioDias.AsNoTracking().SingleAsync(d => d.Fecha == new DateOnly(2026, 10, 14), ct));
        (dia.EsFestivo, dia.EsHabil, dia.Origen).Should().Be((false, true, "MANUAL"));
    }
}
