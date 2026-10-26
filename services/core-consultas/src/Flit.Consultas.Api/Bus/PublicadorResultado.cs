using System.Text.Json;
using Flit.Platform.Sdk.Messaging;
using Google.Protobuf;
using Grpc.Core;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Publica el resultado de un trabajo del diagnóstico (ADR-0075, Q2 = 2a): <c>Enqueue</c> de
/// <see cref="ResultadoConsultaDiagnostico.Tipo"/> en el exchange propio <c>flit.consultas</c> (el usuario <c>consultas</c> no
/// necesita destino cruzado), con la empresa del trabajo. La respuesta pasa por <see cref="MinimizadorRespuesta"/> y viaja en
/// proto-JSON (<see cref="JsonFormatter"/>), en claro (U1 = b). Lo guarda la outbox del <c>DbContext</c> del scope: se confirma o se
/// revierte con la transacción del consumidor, junto con el consumo medido y la bandeja.
/// </summary>
/// <example>
/// <code>
/// publicador.Publicar(sobre.TenantId, trabajo, await ejecutor.EjecutarAsync(trabajo, caller, ct));
/// </code>
/// </example>
internal sealed class PublicadorResultado(IPlatformOutbox outbox)
{
    public EventEnvelope Publicar(Guid tenantId, TrabajoConsultaDiagnostico trabajo, EjecucionTrabajo ejecucion)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        ArgumentNullException.ThrowIfNull(ejecucion);
        var datos = ejecucion is { Error: null, Respuesta: { } respuesta }
            ? ResultadoConsultaDiagnostico.Exito(trabajo, ProtoJson(MinimizadorRespuesta.Minimizar(trabajo.Rpc, respuesta)), ejecucion.Duracion, ejecucion.DesdeCache)
            : ResultadoConsultaDiagnostico.Fallo(trabajo, ejecucion.Error ?? StatusCode.Internal, ejecucion.Duracion);
        return outbox.Enqueue(ResultadoConsultaDiagnostico.Tipo, ResultadoConsultaDiagnostico.VersionContrato, tenantId, datos);
    }

    private static JsonElement ProtoJson(IMessage mensaje)
    {
        using var json = JsonDocument.Parse(JsonFormatter.Default.Format(mensaje));
        return json.RootElement.Clone();
    }
}
