using System.Text.Json;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// Outbox de prueba que solo arma el sobre (HU #14087 AC8): la prueba lee su <c>data</c> como lo leería core-diagnostico, sin
/// base ni broker.
/// <para>Uso de ejemplo: <c>new PublicadorResultado(new OutboxCapturada()).Publicar(empresa, trabajo, ejecucion).DataAs&lt;ResultadoConsultaDiagnostico&gt;()</c>.</para>
/// </summary>
internal sealed class OutboxCapturada : IPlatformOutbox
{
    public EventEnvelope Enqueue<TData>(string type, int version, Guid tenantId, TData data) =>
        new(Guid.CreateVersion7(), type, version, DateTimeOffset.UtcNow, tenantId, "consultas", "corr-prueba", JsonSerializer.SerializeToElement(data, EventEnvelope.JsonOptions));

    public EventEnvelope EnqueueJob<TData>(string type, int version, Guid tenantId, TData data) => Enqueue(type, version, tenantId, data);
}
