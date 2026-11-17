using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// <c>data</c> de <c>consultas.resultado.diagnostico</c> v1 (ADR-0075, Q2 = 2a, U1 = b): lo publica core-consultas en su exchange
/// (<c>flit.consultas</c>) por cada trabajo <c>consultas.diagnostico[-express].&lt;fuente&gt;</c> que atendió, y lo consume la cola
/// <c>diagnostico.consultas-resultados</c> (<see cref="ConsumidorResultados"/>). Con <see cref="Estado"/> OK trae
/// <see cref="Respuesta"/>: el <c>*Response</c> proto COMPLETO del rpc en proto-JSON (HU #14087 AC8: sin minimizar, con los nombres
/// del .proto y los valores por defecto; trae datos personales), en claro en el broker y cifrado al guardarlo
/// (<see cref="RespuestaFuenteCelda"/>); con ERROR trae <see cref="GrpcStatus"/>: el nombre del <see cref="StatusCode"/> que hoy viajaría en el
/// <c>RpcException</c>, nunca el mensaje ni el detalle del proveedor. <see cref="MotivoFallo"/> (opcional; ausente en mensajes viejos)
/// dice por qué no respondió el proveedor (<c>CREDENCIALES|LIMITE|RECHAZO|CAIDA|RED|TIMEOUT</c>). Lo valida el fixture compartido
/// <c>contracts/fixtures/bus/resultado-consulta-diagnostico.v1.json</c>.
/// </summary>
/// <example>
/// <code>
/// var respuesta = sobre.DataAs&lt;ResultadoConsultaDiagnostico&gt;().Leer(sobre.OccurredAt); // RespuestaConsulta
/// </code>
/// </example>
public sealed record ResultadoConsultaDiagnostico(
    Guid TrabajoId, Guid ConsultaId, Guid? PersonaId, string Fuente, string Estado, string? GrpcStatus, JsonElement? Respuesta, long DuracionMs,
    bool DesdeCache, string? MotivoFallo = null)
{
    /// <summary>Tipo del sobre y llave de ruteo en <c>flit.consultas</c>.</summary>
    public const string Tipo = "consultas.resultado.diagnostico";

    /// <summary>Versión del sobre de este evento.</summary>
    public const int VersionContrato = 1;

    public const string EstadoOk = "OK";
    public const string EstadoError = "ERROR";

    /// <summary>
    /// Lector del proto-JSON de la respuesta: tolera campos que core-consultas agregue después (el contrato de los proto crece sin
    /// romper a Diagnóstico); acepta los nombres del .proto (snake_case, los que publica core-consultas) y los lowerCamelCase.
    /// </summary>
    private static readonly JsonParser Lector = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    /// <summary>
    /// Lo que respondió core-consultas como <see cref="RespuestaConsulta"/>: el <c>*Response</c> de la fuente (null si no se pudo
    /// leer) o el código gRPC (desconocido si no se reconoce). <paramref name="terminadoEn"/> es el <c>occurredAt</c> del sobre: el
    /// fin de la consulta; el inicio es ese instante menos <see cref="DuracionMs"/>.
    /// </summary>
    public RespuestaConsulta Leer(DateTimeOffset terminadoEn)
    {
        var inicio = terminadoEn - TimeSpan.FromMilliseconds(Math.Max(DuracionMs, 0));
        if (string.Equals(Estado, EstadoOk, StringComparison.OrdinalIgnoreCase))
            return new RespuestaConsulta(PersonaId, Fuente, LeerRespuesta(), null, inicio, terminadoEn, DesdeCache, MotivoFallo);

        return new RespuestaConsulta(PersonaId, Fuente, null, Codigo(GrpcStatus), inicio, terminadoEn, DesdeCache);
    }

    /// <summary>El mensaje proto de respuesta de cada fuente del bus; null si la fuente no es del diagnóstico.</summary>
    internal static MessageDescriptor? Descriptor(string fuente) => fuente switch
    {
        FuentesBus.Runt => ConsultarVehiculoIntempoResponse.Descriptor,
        FuentesBus.RuntPropietario or FuentesBus.RuntLocatario => ConsultarConductorResponse.Descriptor,
        FuentesBus.Rues => ConsultarRuesResponse.Descriptor,
        FuentesBus.SimitVehiculo or FuentesBus.SimitPropietario or FuentesBus.SimitLocatario => ConsultarMultasResponse.Descriptor,
        FuentesBus.Avaluo => ConsultarAvaluosResponse.Descriptor,
        FuentesBus.Impuesto => ConsultarImpuestoVehicularResponse.Descriptor,
        _ => null,
    };

    /// <summary>El código gRPC por su nombre (<c>Unavailable</c> o <c>UNAVAILABLE</c>); <see cref="StatusCode.Unknown"/> si no se reconoce.</summary>
    internal static StatusCode Codigo(string? grpcStatus)
    {
        var nombre = grpcStatus?.Replace("_", string.Empty, StringComparison.Ordinal).Trim();
        return Enum.TryParse<StatusCode>(nombre, ignoreCase: true, out var codigo) && Enum.IsDefined(codigo) ? codigo : StatusCode.Unknown;
    }

    private IMessage? LeerRespuesta()
    {
        if (Respuesta is not { ValueKind: JsonValueKind.Object } json || Descriptor(Fuente) is not { } descriptor)
            return null;
        try
        {
            return Lector.Parse(json.GetRawText(), descriptor);
        }
        catch (InvalidJsonException)
        {
            return null;
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }
}
