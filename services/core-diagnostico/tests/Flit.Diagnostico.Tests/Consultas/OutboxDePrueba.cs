using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>Una fila de <c>diagnostico.outbox</c> con su sobre ya leído.</summary>
public sealed record MensajeOutbox(string Exchange, string RoutingKey, EventEnvelope Sobre, string Payload)
{
    public JsonElement Data => Sobre.Data;

    public Guid TrabajoId => Data.GetProperty("trabajoId").GetGuid();

    public Guid ConsultaId => Data.GetProperty("consultaId").GetGuid();
}

/// <summary>
/// La outbox de Diagnóstico en las pruebas (HU #13727, ADR-0075): en las pruebas el broker es inalcanzable
/// (<c>amqp://…@127.0.0.1:1</c>), así que lo que se encoló sigue ahí y se lee tal cual, en orden de creación (UUIDv7).
/// </summary>
/// <example>
/// <code>
/// var trabajos = await OutboxDePrueba.TrabajosAsync(api, ct);
/// trabajos.Should().OnlyContain(t => t.RoutingKey == "consultas.diagnostico.runt");
/// </code>
/// </example>
internal static class OutboxDePrueba
{
    /// <summary>Todas las filas de la outbox, en orden de creación.</summary>
    public static async Task<List<MensajeOutbox>> TodosAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        var filas = await db.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        return [.. filas.Select(f => new MensajeOutbox(f.Exchange, f.RoutingKey, EventEnvelope.FromJson(Encoding.UTF8.GetBytes(f.Payload)), f.Payload))];
    }

    /// <summary>Los trabajos de consulta que Diagnóstico dejó a Consultas (<c>flit.consultas</c>).</summary>
    public static async Task<List<MensajeOutbox>> TrabajosAsync(ApiDePrueba api, CancellationToken ct) =>
        [.. (await TodosAsync(api, ct)).Where(m => m.Exchange == "flit.consultas")];

    /// <summary>Los eventos de Diagnóstico de un tipo (<c>flit.diagnostico</c>).</summary>
    public static async Task<List<MensajeOutbox>> EventosAsync(ApiDePrueba api, string tipo, CancellationToken ct) =>
        [.. (await TodosAsync(api, ct)).Where(m => m.Exchange == "flit.diagnostico" && m.RoutingKey == tipo)];

    /// <summary>Un scope de la API con la empresa fijada, como el del procesador (el contexto y la outbox la comparten).</summary>
    public static AsyncServiceScope Scope(ApiDePrueba api, Guid empresa)
    {
        var scope = api.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
        return scope;
    }
}
