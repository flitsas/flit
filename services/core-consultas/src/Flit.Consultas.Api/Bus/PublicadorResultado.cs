using System.Text.Json;
using Flit.Platform.Sdk.Messaging;
using Google.Protobuf;
using Grpc.Core;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Publica el resultado de un trabajo del diagnóstico (ADR-0075, Q2 = 2a): <c>Enqueue</c> de
/// <see cref="ResultadoConsultaDiagnostico.Tipo"/> en el exchange propio <c>flit.consultas</c> (el usuario <c>consultas</c> no
/// necesita destino cruzado), con la empresa del trabajo. Desde HU #14087 (AC8, decisión del usuario 2026-10-27, enmienda de
/// ADR-0075 en Propuesto) la respuesta viaja COMPLETA, sin proyección: el <c>*Response</c> del rpc tal cual en proto-JSON
/// (<see cref="Formato"/>: nombres de campo del .proto en snake_case y valores por defecto incluidos, para que un <c>false</c>, un
/// <c>0</c>, un <c>""</c> o una lista vacía no desaparezcan), en claro (U1 = b). Incluye datos personales (nombres, direcciones,
/// contacto, histórico, terceros): core-diagnostico la guarda cifrada. Un ERROR sigue sin respuesta ni mensaje del proveedor,
/// solo el <c>grpcStatus</c>. Lo guarda la outbox del <c>DbContext</c> del scope: se confirma o se revierte con la transacción del
/// consumidor, junto con el consumo medido y la bandeja.
/// </summary>
/// <example>
/// <code>
/// publicador.Publicar(sobre.TenantId, trabajo, await ejecutor.EjecutarAsync(trabajo, caller, ct));
/// </code>
/// </example>
internal sealed class PublicadorResultado(IPlatformOutbox outbox)
{
    /// <summary>proto-JSON de la respuesta publicada: nombres del .proto (snake_case) y valores por defecto incluidos.</summary>
    internal static JsonFormatter Formato { get; } =
        new(JsonFormatter.Settings.Default.WithFormatDefaultValues(true).WithPreserveProtoFieldNames(true));

    public EventEnvelope Publicar(Guid tenantId, TrabajoConsultaDiagnostico trabajo, EjecucionTrabajo ejecucion)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        ArgumentNullException.ThrowIfNull(ejecucion);
        var datos = ejecucion is { Error: null, Respuesta: { } respuesta }
            ? ResultadoConsultaDiagnostico.Exito(trabajo, ProtoJson(respuesta), ejecucion.Duracion, ejecucion.DesdeCache)
            : ResultadoConsultaDiagnostico.Fallo(trabajo, ejecucion.Error ?? StatusCode.Internal, ejecucion.Duracion);
        return outbox.Enqueue(ResultadoConsultaDiagnostico.Tipo, ResultadoConsultaDiagnostico.VersionContrato, tenantId, datos);
    }

    private static JsonElement ProtoJson(IMessage mensaje)
    {
        using var json = JsonDocument.Parse(Formato.Format(mensaje));
        return json.RootElement.Clone();
    }
}
