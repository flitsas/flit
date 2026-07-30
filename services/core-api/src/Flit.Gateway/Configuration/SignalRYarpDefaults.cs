namespace Flit.Gateway.Configuration;

/// <summary>
/// Constantes de wiring SignalR vía YARP (Feature #11076 / HU #11104 / ADR-0039).
/// La config ejecutable vive en <c>appsettings.json</c>; estas constantes documentan el contrato AC.
/// </summary>
public static class SignalRYarpDefaults
{
    public const string RouteId = "signalr-route";
    public const string ClusterId = "core-api-signalr-cluster";
    public const string HubPathPrefix = "/hubs/";
    public const string AffinityCookieName = ".Flit.SignalR.Affinity";
    public const string AffinityPolicy = "Cookie";
    public const string FailurePolicy = "Redistribute";
    public const string ActivityTimeout = "00:05:00";
}
