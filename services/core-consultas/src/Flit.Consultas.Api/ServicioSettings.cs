namespace Flit.Consultas.Api;

/// <summary>
/// Lo que el servicio necesita para arrancar (HU #13340 AC2). Si falta algo, no arranca y dice qué variable de entorno
/// poner y de qué variable del <c>.env</c> sale en el compose, en vez de fallar después en la primera llamada.
/// </summary>
internal static class ServicioSettings
{
    public const string Codigo = "consultas";

    /// <summary>Puerto interno solo HTTP/2 del gRPC (sin publicar, contrato v1.3 §11).</summary>
    public const string GrpcPortKey = "Servicio:GrpcPort";

    private static readonly (string Key, string EnvFile)[] Required =
    [
        ("ConnectionStrings:Servicio", "CONNECTION_STRING_CONSULTAS"),
        ("Platform:ServiceClient:ClientSecret", "SVC_CONSULTAS_CLIENT_SECRET"),
        ("Platform:ServiceClient:TokenEndpoint", "valor del compose"),
        ("Platform:Auth:JwksUri", "valor del compose"),
        ("Platform:Auth:Issuers:0", "FLIT_HUB_URL"),
        ("Platform:Messaging:ConnectionString", "RABBITMQ_URL"),
        (GrpcPortKey, "CORE_CONSULTAS_GRPC_PORT"),
    ];

    public static void Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var missing = Required
            .Where(r => string.IsNullOrWhiteSpace(configuration[r.Key]))
            .Select(r => $"{r.Key.Replace(":", "__", StringComparison.Ordinal)} (en el .env: {r.EnvFile})")
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"core-{Codigo} no puede arrancar; falta configuración: {string.Join("; ", missing)}. Ver docs/suite/servicio-nuevo.md.");
        }
    }
}
