using System.Runtime.CompilerServices;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// Prepara el entorno ANTES de que arranque cualquier host de pruebas.
/// <para>
/// Los tests levantan el host real sin un PostgreSQL accesible y solo verifican rutas y
/// autorización — la misma disciplina que <c>Flit.Admin.Tests</c>. Fingir la base con un mock
/// probaría el mock, no el SQL, y el SQL es donde han vivido todos los fallos reales de esta
/// herramienta; eso se valida a mano con un <c>--dry-run</c> contra una copia.
/// </para>
/// </summary>
internal static class TestEnvironment
{
    /// <summary>
    /// Sin esto el <c>MigracionSchemaInitializer</c> intentaría crear el esquema al arrancar y el
    /// host no levantaría. Es el equivalente local del <c>Database__AutoMigrate=false</c> que usan
    /// los tests de Flit.Api.
    /// </summary>
    [ModuleInitializer]
    public static void SkipSchemaCheck() =>
        Environment.SetEnvironmentVariable("FLITMIG_MigracionApi__RunSchemaCheckAtStartup", "false");

    /// <summary>Cadenas de conexión de mentira: no se abre ninguna en estos tests.</summary>
    [ModuleInitializer]
    public static void FakeConnections()
    {
        Environment.SetEnvironmentVariable(
            "FLITMIG_ConnectionStrings__V1Source", "Host=localhost;Database=no-existe;Username=x");
        Environment.SetEnvironmentVariable(
            "FLITMIG_ConnectionStrings__V2Target", "Host=localhost;Database=no-existe;Username=x");
    }

    internal const string LlaveValida = "llave-de-prueba-suficientemente-larga";
}
