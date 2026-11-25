using Flit.Impuestos.Api.Modulos.Avisos;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (revisión previa al PR: security B-3) — <see cref="PurgaAvisosRecibidos"/> borra por lotes las filas de
/// <c>impuestos.avisos_recibidos</c> con más de 30 días (<see cref="PurgaAvisosRecibidos.ClaveRetencion"/>), cada
/// <see cref="PurgaAvisosRecibidos.ClaveIntervalo"/>, y deja las recientes. Corre como trabajo en segundo plano.
/// </summary>
/// <example>
/// <code>
/// var borradas = await api.App.Services.GetRequiredService&lt;PurgaAvisosRecibidos&gt;().PurgarAsync(ct);
/// </code>
/// </example>
public sealed class PurgaAvisosRecibidosTests
{
    [Fact]
    public async Task Borra_por_lotes_lo_que_tiene_mas_de_30_dias_y_deja_lo_reciente()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await ApiDePrueba.ConBaseAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [PurgaAvisosRecibidos.ClaveLote] = "2" });
            b.Services.AddSingleton<TimeProvider>(reloj);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await using (var db = api.Contexto(null))
        {
            foreach (var (id, dias) in new[] { ("viejo-1", 31), ("viejo-2", 40), ("viejo-3", 90), ("reciente", 29) })
                db.Add(new AvisoRecibido { DeliveryId = id, RecibidoEn = reloj.Ahora.AddDays(-dias), Resultado = "APLICADO" });
            await db.SaveChangesAsync(ct);
        }

        var purga = api.App.Services.GetRequiredService<PurgaAvisosRecibidos>();
        var borradas = await purga.PurgarAsync(ct);

        borradas.Should().Be(3, "tres filas pasan de 30 días (en dos lotes de 2)");
        await using var lectura = api.Contexto(null);
        (await lectura.Set<AvisoRecibido>().AsNoTracking().Select(a => a.DeliveryId).ToListAsync(ct)).Should().Equal("reciente");
        api.App.Services.GetServices<IHostedService>().OfType<PurgaAvisosRecibidos>().Should().ContainSingle("corre en segundo plano");
    }
}
