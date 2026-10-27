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

    /// <summary>
    /// Fuerza el proveedor OCR a mock en las pruebas. El host lee env vars CRUDAS (OCR_PROVIDER tiene
    /// prioridad sobre appsettings), así los tests de integración del OCR son deterministas y NUNCA
    /// llaman a Anthropic real, sin importar el <c>appsettings.Development.json</c> local del desarrollador.
    /// </summary>
    [ModuleInitializer]
    public static void ForceOcrMock()
    {
        Environment.SetEnvironmentVariable("OCR_PROVIDER", "mock");
    }

    /// <summary>
    /// HU #13348 (Epic #13316): core-api llama a core-consultas siempre, así que el host exige el secreto del cliente de
    /// servicio svc-tramites al arrancar. En las pruebas basta uno cualquiera: ninguna llama al servicio de verdad
    /// (las que prueban la llamada reemplazan el cliente gRPC).
    /// </summary>
    [ModuleInitializer]
    public static void ServiceClientDePrueba()
    {
        Environment.SetEnvironmentVariable("Platform__ServiceClient__ClientSecret", "secreto-de-pruebas");
    }

    /// <summary>
    /// Los controles de permisos vienen encendidos por defecto en la API. Las pruebas que no los ejercitan se
    /// escribieron con el comportamiento previo (solo la policy de rol): aquí arrancan apagados y las pruebas de exigencia
    /// los encienden explícitamente con <c>UseSetting</c>/configuración en memoria, que tiene prioridad sobre estas variables.
    /// </summary>
    [ModuleInitializer]
    public static void ControlesDePermisosApagadosSalvoQueLaPruebaLosEncienda()
    {
        Environment.SetEnvironmentVariable("Security__OtPermissionEnforcement__Enabled", "false");
        Environment.SetEnvironmentVariable("Security__ModulePermissionEnforcement__Enabled", "false");
    }
}
