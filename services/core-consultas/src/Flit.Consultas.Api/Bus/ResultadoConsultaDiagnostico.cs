using System.Text.Json;
using Grpc.Core;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// <c>data</c> del evento <c>consultas.resultado.diagnostico</c> v1 (ADR-0075, Q2 = 2a): el resultado de UNA ejecución del trabajo
/// (U2 = a: Consultas no reintenta). <see cref="Estado"/> es <see cref="Ok"/> con la <see cref="Respuesta"/> (el <c>*Response</c>
/// proto en proto-JSON, ya minimizado por <see cref="MinimizadorRespuesta"/>) o <see cref="Error"/> con el <see cref="GrpcStatus"/>
/// (el nombre del <see cref="StatusCode"/> que viajaría en la <see cref="RpcException"/>). Nunca lleva el mensaje de la excepción ni
/// el detalle del proveedor. Espejo del record de core-diagnostico, validado contra
/// <c>contracts/fixtures/bus/resultado-consulta-diagnostico.v1.json</c>.
/// </summary>
public sealed record ResultadoConsultaDiagnostico(
    Guid TrabajoId,
    Guid ConsultaId,
    Guid? PersonaId,
    string Fuente,
    string Estado,
    string? GrpcStatus,
    JsonElement? Respuesta,
    int DuracionMs,
    bool DesdeCache)
{
    /// <summary>Tipo (y llave de ruteo) del evento, en el exchange propio <c>flit.consultas</c>.</summary>
    public const string Tipo = "consultas.resultado.diagnostico";

    public const int VersionContrato = 1;

    public const string Ok = "OK";

    public const string Error = "ERROR";

    /// <summary>Resultado OK del <paramref name="trabajo"/> con la respuesta ya minimizada en proto-JSON.</summary>
    public static ResultadoConsultaDiagnostico Exito(TrabajoConsultaDiagnostico trabajo, JsonElement respuesta, TimeSpan duracion, bool desdeCache)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        return new(trabajo.TrabajoId, trabajo.ConsultaId, trabajo.PersonaId, trabajo.Fuente, Ok, null, respuesta, Milisegundos(duracion), desdeCache);
    }

    /// <summary>Resultado ERROR del <paramref name="trabajo"/> con el código gRPC, sin respuesta.</summary>
    public static ResultadoConsultaDiagnostico Fallo(TrabajoConsultaDiagnostico trabajo, StatusCode codigo, TimeSpan duracion)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        return new(trabajo.TrabajoId, trabajo.ConsultaId, trabajo.PersonaId, trabajo.Fuente, Error, codigo.ToString(), null, Milisegundos(duracion), false);
    }

    private static int Milisegundos(TimeSpan duracion) => (int)Math.Clamp(duracion.TotalMilliseconds, 0, int.MaxValue);
}
