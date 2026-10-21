using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// HU #13784 (hallazgo B16-1): SIMIT y la fuente municipal escriben 01-01-2000 cuando no tienen la fecha de notificación.
/// Como fecha real daba comparendos «Prescritos» sin avisos ni descuentos.
/// </summary>
public sealed class FechaVaciaFuentesTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly DateOnly Vacia = new(2000, 1, 1);

    [Fact]
    public async Task AC1_AC2_LaIngestaIgnoraLaFechaVacia_YConservaLaReal()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var ids = await IngresarAsync(api, empresa, Item("V-1", notificacion: Vacia), Item("V-2", notificacion: new DateOnly(2026, 10, 1)));

        var vacia = await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["V1"], ct));
        (vacia.FechaNotificacion, vacia.FechaFinDescuento50, vacia.FechaPrescripcion).Should().Be(((DateOnly?)null, (DateOnly?)null, (DateOnly?)null),
            "una fotomulta sin notificación no corre ningún plazo");
        var real = await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["V2"], ct));
        real.FechaNotificacion.Should().Be(new DateOnly(2026, 10, 1));
        real.FechaFinDescuento50.Should().NotBeNull();
    }

    [Fact]
    public async Task AC3_LosYaGuardados_SeCorrigenUnaSolaVez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("V-3", notificacion: new DateOnly(2026, 10, 1))))["V3"];
        // Como quedaban antes del arreglo: la fecha falsa y los plazos calculados con ella.
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(c => c.FechaNotificacion, Vacia).SetProperty(c => c.FechaPrescripcion, new DateOnly(2003, 1, 1)), ct));

        async Task<int> Corregir()
        {
            await using var scope = api.App.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            return await CorreccionFechaVacia.AplicarAsync(sp.GetRequiredService<ComparendosDb>(), sp.GetRequiredService<CalendarioService>(),
                sp.GetRequiredService<Timeline>(), sp.GetRequiredService<Reloj>(), ct);
        }

        (await Corregir()).Should().BeGreaterThanOrEqualTo(1);
        var c = await api.EnDbAsync(db => db.Comparendos.SingleAsync(x => x.Id == id, ct));
        (c.FechaNotificacion, c.FechaPrescripcion, c.FechaFinDescuento50).Should().Be(((DateOnly?)null, (DateOnly?)null, (DateOnly?)null));
        (await api.EnDbAsync(db => db.Timeline.CountAsync(e => e.ComparendoId == id && e.Descripcion.Contains("01-01-2000"), ct))).Should().Be(1);

        (await Corregir()).Should().Be(0, "solo toca los que aún tienen la fecha vacía");
        (await api.EnDbAsync(db => db.Timeline.CountAsync(e => e.ComparendoId == id && e.Descripcion.Contains("01-01-2000"), ct))).Should().Be(1);
    }
}
