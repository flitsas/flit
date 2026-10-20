using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>D6 (2026-10-13): la fuente sobrescribe lo que trae (Excel C2) y no borra lo que no trae.</summary>
public sealed class FuenteNoBorraTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    [Fact]
    public async Task LoQueLaFuenteNoTrae_SeConserva_YLoQueTrae_SeActualiza()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var completo = Item("NB-1", valor: 604100m) with { HoraImposicion = new TimeOnly(8, 30), DireccionInfraccion = "CALLE 10 # 20-30" };
        var id = (await IngresarAsync(api, empresa, completo)).Values.Single();
        var nit = await api.EnDbAsync(db => db.IdentificacionesMonitoreadas.Where(n => n.TenantId == empresa).Select(n => n.NumeroBase).SingleAsync(ct));

        // La siguiente corrida trae el valor nuevo pero sin documento, hora ni dirección.
        api.Fuente.Respuestas[$"SimitNit:{nit}"] = () => RespuestaFuente.Exitosa([completo with
        {
            ValorTotal = 650000m, NumeroDocumentoInfractor = null, TipoDocumentoInfractor = null, HoraImposicion = null, DireccionInfraccion = null,
        }]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        var c = await api.EnDbAsync(db => db.Comparendos.AsNoTracking().SingleAsync(x => x.Id == id, ct));
        c.ValorTotal.Should().Be(650000m, "lo que trae la fuente se sobrescribe");
        (c.NumeroDocumentoInfractor, c.TipoDocumentoInfractor, c.HoraImposicion, c.DireccionInfraccion)
            .Should().Be(("900123456", (TipoDocumento?)TipoDocumento.NIT, (TimeOnly?)new TimeOnly(8, 30), "CALLE 10 # 20-30"), "lo que no trae no se borra");
    }
}
