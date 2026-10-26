using System.Text.Json;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Qué se consulta, tal como va en la llave de ruteo del trabajo (<c>consultas.diagnostico[-express].&lt;fuente&gt;</c>, ADR-0075,
/// <c>contracts/asyncapi/consultas-events.v1.yaml</c>). Espejo de <c>FuentesBus</c> de core-diagnostico.
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
}

/// <summary>
/// El rpc de core-consultas (<c>contracts/proto/flit/consultas/v1</c>) que atiende cada trabajo: <c>Servicio/Método</c>. Espejo de
/// <c>RpcConsultas</c> de core-diagnostico y del enum <c>rpc</c> del AsyncAPI.
/// </summary>
public static class RpcConsultas
{
    public const string PorPlaca = "RuntIntempoService/ConsultarPorPlaca";
    public const string PorVin = "RuntIntempoService/ConsultarPorVin";
    public const string Conductor = "ConsultasService/ConsultarConductor";
    public const string Rues = "ConsultasService/ConsultarRues";
    public const string Multas = "ConsultasService/ConsultarMultas";
    public const string Avaluos = "ConsultasService/ConsultarAvaluos";
    public const string Impuesto = "ConsultasService/ConsultarImpuestoVehicular";

    /// <summary>Los siete rpcs del contrato v1.</summary>
    public static IReadOnlyList<string> Todos { get; } = [PorPlaca, PorVin, Conductor, Rues, Multas, Avaluos, Impuesto];
}

/// <summary>
/// <c>data</c> del trabajo <c>consultas.diagnostico[-express].&lt;fuente&gt;</c> v1 que deja Diagnóstico (ADR-0075, Q1, U1 = b): ids para
/// correlacionar (<see cref="TrabajoId"/> es el trabajo vigente de la celda), la fuente, el rpc y la <see cref="Solicitud"/> (el
/// <c>*Request</c> proto en proto-JSON, en claro y mínimo). Espejo del record de core-diagnostico, validado contra el fixture
/// compartido <c>contracts/fixtures/bus/trabajo-consulta-diagnostico.v1.json</c>. Nunca se registra en logs: lleva placa, VIN y
/// documentos.
/// </summary>
public sealed record TrabajoConsultaDiagnostico(Guid TrabajoId, Guid ConsultaId, Guid? PersonaId, string Fuente, string Rpc, JsonElement Solicitud)
{
    /// <summary>Prefijo de la llave de los trabajos de los lotes.</summary>
    public const string Prefijo = "consultas.diagnostico";

    /// <summary>Prefijo de la llave de los trabajos del Express.</summary>
    public const string PrefijoExpress = "consultas.diagnostico-express";

    /// <summary>Cola de los lotes (FIFO del broker, sin cupo por empresa: D2).</summary>
    public const string ColaLote = "consultas.diagnostico";

    /// <summary>Cola aparte del Express (brecha 3), con su propio consumidor.</summary>
    public const string ColaExpress = "consultas.diagnostico-express";

    /// <summary>Versión del sobre de este trabajo.</summary>
    public const int VersionContrato = 1;

    /// <summary>
    /// Llave con comodín de la cola (<c>consultas.diagnostico.*</c> o <c>consultas.diagnostico-express.*</c>): todas las fuentes de
    /// esa cola. En un exchange topic <c>diagnostico-express</c> es otra palabra, así que los lotes no reciben los del Express.
    /// </summary>
    public static string Patron(bool esExpress) => $"{(esExpress ? PrefijoExpress : Prefijo)}.*";
}
