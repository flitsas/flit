using System.Net;
using System.Net.Http.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Concurrencia;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Api;

/// <summary>
/// HU #14694 (revisión previa al PR: MEN-7, decisión del usuario) — relanzar una placa se permite varias veces, pero en serie: la
/// transacción que crea el relanzamiento toma <c>SELECT … FOR UPDATE</c> del ítem original (con su compañía). Dos relanzamientos
/// simultáneos del mismo ítem esperan su turno y cada uno crea su individual, sin error.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~RelanzarConcurrenteTests
/// </code>
/// </example>
public sealed class RelanzarConcurrenteTests
{
    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];
    private static readonly object Confirmado = new { confirmarPosibleDuplicado = true };

    [Fact]
    public async Task Dos_relanzamientos_simultaneos_del_mismo_item_se_hacen_en_serie_y_cada_uno_crea_el_suyo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["AAA000", "AAA001"], ct, EstadoItem.Exitoso);
        var item = items[1];
        await using (var db = api.Contexto(empresa))
        {
            await db.Set<LiquidacionItem>().Where(i => i.Id == item)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, EstadoItem.InciertoRequiereDecision), ct);
            await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.CompletadoConNovedades), ct);
        }

        // La prueba retiene la fila del ítem original con FOR NO KEY UPDATE: no choca con el KEY SHARE de la FK relanzado_desde
        // (sin el FOR UPDATE del relanzamiento nadie esperaría), sí con el FOR UPDATE que toma cada relanzamiento.
        var (conexion, bloqueo) = await Bloqueos.RetenerAsync(api.BaseDeDatos, "SELECT 1 FROM impuestos.liquidacion_items WHERE id = $1 FOR NO KEY UPDATE", ct, item);
        await using var _ = conexion;
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));
        var ruta = $"{Program.Prefijo}/liquidaciones/{lote}/items/{item}/relanzar";
        var primero = cliente.PostAsJsonAsync(ruta, Confirmado, ct);
        var segundo = cliente.PostAsJsonAsync(ruta, Confirmado, ct);

        var enEspera = await Bloqueos.EsperarSesionesBloqueadasAsync(api.BaseDeDatos, 2, TimeSpan.FromSeconds(10), ct);
        await bloqueo.CommitAsync(ct);
        var respuestas = await Task.WhenAll(primero, segundo).WaitAsync(TimeSpan.FromSeconds(30), ct);

        enEspera.Should().BeTrue("los dos relanzamientos esperan el bloqueo del ítem original (en serie)");
        foreach (var r in respuestas)
            r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync(ct));
        await using var lectura = api.Contexto(empresa);
        var relanzados = await lectura.Set<LiquidacionItem>().AsNoTracking().Where(i => i.RelanzadoDesde == item).ToListAsync(ct);
        relanzados.Should().HaveCount(2, "cada relanzamiento crea el suyo");
        relanzados.Select(i => i.LiquidacionId).Distinct().Should().HaveCount(2);
        (await M.LotesAsync(api, empresa, ct)).Should().Be(3);
    }
}
