using System.Net;
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
/// HU #14694 (R3, decisión del usuario 2026-11-26) — relanzar abre un nuevo intento del MISMO ítem dentro de una transacción con el
/// candado del crédito, la cabecera y el ítem bloqueados (<c>FOR UPDATE</c>). Dos relanzamientos simultáneos del mismo ítem quedan en
/// serie: el primero abre el intento 2 (200) y el segundo encuentra el ítem en vuelo y responde 409 <c>NO_RELANZABLE</c>. Nunca dos
/// intentos ni dos eventos.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~RelanzarConcurrenteTests
/// </code>
/// </example>
public sealed class RelanzarConcurrenteTests
{
    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];

    [Fact]
    public async Task Dos_relanzamientos_simultaneos_del_mismo_item_se_hacen_en_serie_y_solo_uno_abre_intento()
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

        // La prueba retiene la fila del ítem: el primero toma el candado del crédito y espera el ítem; el segundo espera el candado.
        var (conexion, bloqueo) = await Bloqueos.RetenerAsync(api.BaseDeDatos, "SELECT 1 FROM impuestos.liquidacion_items WHERE id = $1 FOR UPDATE", ct, item);
        await using var _ = conexion;
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Gestor));
        var ruta = $"{Program.Prefijo}/liquidaciones/{lote}/items/{item}/relanzar";
        var primero = cliente.PostAsync(ruta, null, ct);
        var segundo = cliente.PostAsync(ruta, null, ct);

        var enEspera = await Bloqueos.EsperarSesionesBloqueadasAsync(api.BaseDeDatos, 2, TimeSpan.FromSeconds(10), ct);
        await bloqueo.CommitAsync(ct);
        var respuestas = await Task.WhenAll(primero, segundo).WaitAsync(TimeSpan.FromSeconds(30), ct);

        enEspera.Should().BeTrue("los dos relanzamientos esperan en serie");
        respuestas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var conflicto = respuestas.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await conflicto.Content.ReadAsStringAsync(ct)).Should().Contain("NO_RELANZABLE");
        await using var lectura = api.Contexto(empresa);
        (await lectura.Set<LiquidacionItem>().AsNoTracking().SingleAsync(i => i.Id == item, ct)).NumeroIntento.Should().Be(2);
        (await lectura.Set<LiquidacionItemIntento>().CountAsync(i => i.ItemId == item, ct)).Should().Be(1);
        (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().ContainSingle();
        (await M.LotesAsync(api, empresa, ct)).Should().Be(1);
    }
}
