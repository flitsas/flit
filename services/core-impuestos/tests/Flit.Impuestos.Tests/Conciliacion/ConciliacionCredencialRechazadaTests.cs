using System.Net;
using Flit.Impuestos.Api.Modulos.Conciliacion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Conciliacion;

/// <summary>
/// HU #14694 (M10 + M09, F1a-CF12) — un 401 del proveedor al consultar un ítem <c>ENVIADO</c> en la ronda de conciliación lleva el
/// aprovisionamiento de la compañía a <c>REQUIERE_CREDENCIALES</c> (motivo <c>CREDENCIAL_RECHAZADA</c>, historial), igual que al
/// liquidar (<c>CredencialRechazadaEnUso</c>); el ítem sigue <c>ENVIADO</c> y reprogramado. Otro error (5xx) no toca el
/// aprovisionamiento.
/// </summary>
/// <example>
/// <code>
/// await api.App.Services.GetRequiredService&lt;TrabajadorConciliacion&gt;().EjecutarRondaAsync(ct);
/// </code>
/// </example>
public sealed class ConciliacionCredencialRechazadaTests
{
    [Fact]
    public async Task Un_401_en_la_conciliacion_lleva_la_compania_a_REQUIERE_CREDENCIALES()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\"}}") };
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await SembrarEnviadoAsync(api, reloj, ct);

        await api.App.Services.GetRequiredService<TrabajadorConciliacion>().EjecutarRondaAsync(ct);

        var aprovisionamiento = await R.AprovisionamientoAsync(api, empresa, ct);
        aprovisionamiento.Estado.Should().Be(EstadoAprovisionamiento.RequiereCredenciales);
        aprovisionamiento.MotivoCodigo.Should().Be("CREDENCIAL_RECHAZADA");
        await using (var db = api.Contexto(empresa))
        {
            (await db.Set<HistorialAprovisionamiento>().AsNoTracking().SingleAsync(ct)).Hacia.Should().Be(EstadoAprovisionamiento.RequiereCredenciales);
        }

        var fila = await R.ItemAsync(api, empresa, item, ct);
        fila.Estado.Should().Be(EstadoItem.Enviado);
        fila.ProximoPollEn.Should().NotBeNull("sigue reprogramado");
    }

    [Fact]
    public async Task Un_5xx_en_la_conciliacion_no_toca_el_aprovisionamiento()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.ServiceUnavailable, "{}") };
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, _) = await SembrarEnviadoAsync(api, reloj, ct);

        await api.App.Services.GetRequiredService<TrabajadorConciliacion>().EjecutarRondaAsync(ct);

        (await R.AprovisionamientoAsync(api, empresa, ct)).Estado.Should().Be(EstadoAprovisionamiento.Listo);
    }

    private static async Task<(Guid Empresa, Guid Item)> SembrarEnviadoAsync(Flit.Impuestos.Tests.Api.ApiDePrueba api, RelojDePrueba reloj, CancellationToken ct)
    {
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var item = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i =>
        {
            i.ConsultaApiId = "c-401";
            i.EnviadoEn = reloj.Ahora.AddMinutes(-10);
            i.ProximoPollEn = null;
        });
        return (empresa, item.Id);
    }
}
