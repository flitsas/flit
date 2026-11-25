using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Concurrencia;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (revisión previa al PR: code review IMP-4 / DB B8) — carrera entre la suspensión (apagar el producto) y la
/// aplicación del resultado del alta. El alta está en curso cuando llega el apagado; el apagado se confirma mientras el
/// <see cref="Aprovisionador"/> quiere aplicar su resultado. Ninguno pisa al otro: gana <c>SUSPENDIDO</c>, la credencial que trajo el
/// alta se guarda y el estado listo queda como previo (D-DESACT).
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~CarreraSuspensionAltaTests
/// </code>
/// </example>
public sealed class CarreraSuspensionAltaTests
{
    [Fact]
    public async Task La_suspension_confirmada_durante_el_alta_gana_y_la_credencial_se_guarda()
    {
        var ct = TestContext.Current.CancellationToken;
        var altaEnCurso = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var soltarAlta = new ManualResetEventSlim(false);
        var proveedor = EntornoAprovisionamiento.ProveedorConAltas(_ =>
        {
            altaEnCurso.TrySetResult();
            soltarAlta.Wait(TimeSpan.FromSeconds(30));
            return ApiLiquidacion.ApiLiquidacionDePrueba.Json(HttpStatusCode.Created, EntornoAprovisionamiento.Alta());
        });
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        var evaluacion = Task.Run(() => EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct), ct);
        await altaEnCurso.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        // El apagado entra como lo entrega el SDK y deja su transacción abierta (fila actualizada y bloqueada) mientras el alta termina.
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            var consumidor = scope.ServiceProvider.GetRequiredService<ConsumidorCambioProducto>();
            var sobre = EntornoAprovisionamiento.Cambio(empresa, encendido: false);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await consumidor.HandleAsync(sobre, sobre.DataAs<JsonElement>(), ct);
                db.Set<InboxMessage>().Add(new InboxMessage { EventId = sobre.EventId, Consumer = ConsumidorCambioProducto.Cola, Type = sobre.Type, ProcessedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(ct);
                soltarAlta.Set();
                // El aprovisionador llega a su escritura y espera el bloqueo de la fila (o, sin token ni bloqueo, la pisaría).
                await Bloqueos.EsperarSesionesBloqueadasAsync(api.BaseDeDatos, 1, TimeSpan.FromSeconds(5), ct);
                await tx.CommitAsync(ct);
            });
        }

        (await evaluacion.WaitAsync(TimeSpan.FromSeconds(30), ct)).Should().BeTrue();

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Suspendido, "con el producto apagado gana SUSPENDIDO");
        fila.EstadoPrevio.Should().Be(E.Listo, "D-DESACT: el estado listo al que llegó el alta queda como previo");
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct)).Should().NotBeNull("la credencial del alta llega una sola vez y se guarda");
        var historial = await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct);
        historial.Should().ContainSingle(h => h.Hacia == E.Suspendido && h.ActorTipo == ConsumidorCambioProducto.ActorPlataforma);
        historial.Should().NotContain(h => h.Desde == E.Pendiente && h.Hacia == E.Listo, "el alta no pisa la suspensión");
    }
}
