using System.Text.Json;
using Flit.Diagnostico.Domain.Consultas;
using Google.Protobuf;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Qué se consulta, tal como va en la llave de ruteo del trabajo (<c>consultas.diagnostico[-express].&lt;fuente&gt;</c>, ADR-0075,
/// <c>contracts/asyncapi/consultas-events.v1.yaml</c>). No es el <see cref="Domain.Consultas.Fuente"/> de la celda: en las celdas
/// RUNT de persona, la persona con NIT va a <see cref="Rues"/>.
/// </summary>
public static class FuentesBus
{
    public const string Runt = "runt";
    public const string RuntPropietario = "runt_propietario";
    public const string RuntLocatario = "runt_locatario";
    public const string SimitVehiculo = "simit_vehiculo";
    public const string SimitPropietario = "simit_propietario";
    public const string SimitLocatario = "simit_locatario";
    public const string Rues = "rues";
    public const string Avaluo = "avaluo";
    public const string Impuesto = "impuesto";

    /// <summary>La fuente del bus de una celda (sin el caso NIT de las celdas RUNT de persona, que es <see cref="Rues"/>).</summary>
    public static string De(Fuente fuente) => fuente switch
    {
        Fuente.Runt => Runt,
        Fuente.RuntPropietario => RuntPropietario,
        Fuente.RuntLocatario => RuntLocatario,
        Fuente.SimitVehiculo => SimitVehiculo,
        Fuente.SimitPropietario => SimitPropietario,
        Fuente.SimitLocatario => SimitLocatario,
        Fuente.Avaluo => Avaluo,
        Fuente.ImpuestoDepartamental => Impuesto,
        _ => throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "Fuente sin trabajo de consulta."),
    };
}

/// <summary>El rpc de core-consultas (<c>contracts/proto/flit/consultas/v1</c>) que atiende cada trabajo: <c>Servicio/Método</c>.</summary>
public static class RpcConsultas
{
    public const string PorPlaca = "RuntIntempoService/ConsultarPorPlaca";
    public const string PorVin = "RuntIntempoService/ConsultarPorVin";
    public const string Conductor = "ConsultasService/ConsultarConductor";
    public const string Rues = "ConsultasService/ConsultarRues";
    public const string Multas = "ConsultasService/ConsultarMultas";
    public const string Avaluos = "ConsultasService/ConsultarAvaluos";
    public const string Impuesto = "ConsultasService/ConsultarImpuestoVehicular";
}

/// <summary>
/// Una consulta que pide una celda (ADR-0075): la fuente del bus, el rpc y el <c>*Request</c> proto que hoy envía su
/// <c>Fuente*Real</c>. <paramref name="PersonaId"/> es la persona de <c>personas_vehiculo</c> en las celdas de persona (una
/// consulta por persona) y null en las de vehículo.
/// </summary>
public sealed record SolicitudConsulta(string Fuente, string Rpc, IMessage Solicitud, Guid? PersonaId = null);

/// <summary>
/// <c>data</c> del trabajo <c>consultas.diagnostico[-express].&lt;fuente&gt;</c> v1 (ADR-0075, Q1, U1 = b): ids para correlacionar y
/// descartar trabajos viejos (<see cref="TrabajoId"/> es el <c>consultas.trabajo_id</c> vigente de la celda), la fuente, el rpc y
/// la <see cref="Solicitud"/> en proto-JSON y EN CLARO, con solo lo que pide el proveedor (nunca nombres ni direcciones). Lo
/// valida el fixture compartido <c>contracts/fixtures/bus/trabajo-consulta-diagnostico.v1.json</c>.
/// </summary>
public sealed record TrabajoConsultaDiagnostico(Guid TrabajoId, Guid ConsultaId, Guid? PersonaId, string Fuente, string Rpc, JsonElement Solicitud)
{
    /// <summary>Prefijo de la llave de los trabajos de los lotes (cola <c>consultas.diagnostico</c>).</summary>
    public const string Prefijo = "consultas.diagnostico";

    /// <summary>Prefijo de la llave de los trabajos del Express (cola aparte <c>consultas.diagnostico-express</c>).</summary>
    public const string PrefijoExpress = "consultas.diagnostico-express";

    /// <summary>Versión del sobre de este trabajo.</summary>
    public const int VersionContrato = 1;

    /// <summary>Llave de ruteo (y tipo del sobre) del trabajo de <paramref name="fuente"/>.</summary>
    public static string Clave(string fuente, bool esExpress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fuente);
        return $"{(esExpress ? PrefijoExpress : Prefijo)}.{fuente}";
    }

    /// <summary>El trabajo de la celda <paramref name="consultaId"/> con la solicitud en proto-JSON (<see cref="JsonFormatter"/>).</summary>
    public static TrabajoConsultaDiagnostico Crear(Guid trabajoId, Guid consultaId, SolicitudConsulta solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        using var json = JsonDocument.Parse(JsonFormatter.Default.Format(solicitud.Solicitud));
        return new(trabajoId, consultaId, solicitud.PersonaId, solicitud.Fuente, solicitud.Rpc, json.RootElement.Clone());
    }
}
