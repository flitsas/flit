using System.Runtime.CompilerServices;

namespace Flit.Admin.Tests;

/// <summary>
/// Configuración global del proceso de pruebas. Se ejecuta una sola vez al cargar
/// el ensamblado de tests, antes de cualquier <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
internal static class TestEnvironment
{
    /// <summary>
    /// Desactiva la migración automática al arranque (Program.cs) durante las pruebas.
    /// Los tests de integración levantan el host real sin un PostgreSQL accesible y
    /// solo verifican autorización/rutas; no deben intentar conectar ni migrar. En
    /// producción el valor por defecto es <c>true</c>.
    /// </summary>
    [ModuleInitializer]
    public static void DisableAutoMigrate()
    {
        Environment.SetEnvironmentVariable("Database__AutoMigrate", "false");
    }
}
