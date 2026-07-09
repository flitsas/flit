using System.Runtime.CompilerServices;

namespace Flit.Infrastructure.Tests.Telemetry;

/// <summary>
/// Configuración global del proceso de pruebas para los tests de integración de telemetría
/// (Reportes2 HU-A). Mismo patrón que <c>Flit.Admin.Tests.TestEnvironment</c>: el host real se
/// levanta con <c>WebApplicationFactory&lt;Program&gt;</c> sin un PostgreSQL accesible, así que
/// se desactiva la migración automática del arranque.
/// </summary>
internal static class TelemetryTestEnvironment
{
    [ModuleInitializer]
    public static void DisableAutoMigrate()
    {
        Environment.SetEnvironmentVariable("Database__AutoMigrate", "false");
    }
}
