namespace Flit.Api.Hosting;

/// <summary>
/// Papel con el que arranca este programa (HU #13224, Epic #13217). El mismo <c>Flit.Api.dll</c> corre como
/// <c>core-api</c> (todo) o como <c>core-identity</c> (solo lo que el login necesita); ver
/// <c>docs/suite/identidad-frontera.md</c> §3.
/// </summary>
internal enum HostRole
{
    /// <summary><c>core-api</c>: todos los endpoints, migraciones, seeder, gRPC y procesos en segundo plano.</summary>
    Api,

    /// <summary>
    /// <c>core-identity</c>: solo las rutas de identidad, sin migraciones, seeder, gRPC ni procesos de negocio.
    /// </summary>
    Identity,
}

internal static class HostRoles
{
    /// <summary><c>Flit:HostRole</c> (en el compose, <c>Flit__HostRole</c>). Vacía o desconocida: <see cref="HostRole.Api"/>.</summary>
    public const string ConfigKey = "Flit:HostRole";

    public static HostRole From(IConfiguration configuration) =>
        string.Equals(configuration[ConfigKey], "identity", StringComparison.OrdinalIgnoreCase)
            ? HostRole.Identity
            : HostRole.Api;
}
