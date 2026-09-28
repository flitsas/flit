using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Flit.Infrastructure.Security;

/// <summary>
/// HU #12895 (FLIT Suite A-02) — qué siembra <see cref="DevelopmentAuthSeeder"/> al arrancar, separado del nombre del
/// ambiente. Antes todo dependía de <c>IsDevelopment()</c>, y DEV, QA y PDN corren como <c>Development</c>: renombrar
/// DEV lo dejaba sin los permisos nuevos (a-inventario-ambientes.md, puntos 7 a 9).
/// </summary>
/// <param name="RbacCatalog">
/// <c>Seed:RbacCatalog</c>. Roles de sistema, módulos, permisos y catálogo de organismos de tránsito. Por defecto, solo en
/// Development; el compose de los servidores lo enciende siempre.
/// </param>
/// <param name="DemoData">
/// <c>Seed:DemoUsers</c>. Cuentas demo con contraseña fija y sus empresas, más los SQL de datos de prueba. Por defecto,
/// solo en Development. Fuera de local se apaga con <c>FLIT_SEED_DEMO_USERS=false</c> en el <c>.env</c>.
/// </param>
public sealed record SeedSettings(bool RbacCatalog, bool DemoData)
{
    public static SeedSettings From(IConfiguration configuration, IHostEnvironment environment)
    {
        var isDevelopment = environment.IsDevelopment();
        return new SeedSettings(
            configuration.GetValue("Seed:RbacCatalog", isDevelopment),
            configuration.GetValue("Seed:DemoUsers", isDevelopment));
    }

    /// <summary>El comportamiento anterior a A-02: todo o nada según el nombre del ambiente.</summary>
    public static SeedSettings From(IHostEnvironment environment) =>
        new(environment.IsDevelopment(), environment.IsDevelopment());
}
