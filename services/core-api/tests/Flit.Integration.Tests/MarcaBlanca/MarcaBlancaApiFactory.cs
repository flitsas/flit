using Flit.Integration.Tests.Postgres;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 — el ÚNICO host HTTP real (<c>WebApplicationFactory&lt;Program&gt;</c>) de
/// <c>Flit.Integration.Tests</c>: las suites de anti-enumeración (AC2/AC3/AC4) necesitan ejercitar
/// <c>DomainContextMiddleware</c> + <c>DomainBindingMiddleware</c> + el rate limiter reales, no solo
/// el handler en aislado (delta-hechos #31: "sin test HTTP end-to-end con sello — patrón
/// AuthLoginAndMeEndpointsTests + X-Flit-Domain" quedó pendiente en #12422/#12423, cerrado aquí).
/// <para>
/// <b>Por qué variables de entorno y no solo <c>ConfigureAppConfiguration</c></b>: <c>Program.cs</c>
/// lee <c>builder.Configuration.GetConnectionString("Core")</c> en una variable local (<c>coreConnStr</c>)
/// DENTRO de las sentencias top-level del propio archivo, antes de que
/// <c>WebApplicationFactory&lt;Program&gt;.ConfigureWebHost</c> tenga oportunidad de inyectar
/// configuración adicional (el hosting mínimo de ASP.NET Core ejecuta el <c>Program.cs</c> real como
/// parte de <c>CreateHost</c>). Con solo <c>ConfigureAppConfiguration</c>, <c>IConfiguration</c> SÍ
/// refleja el valor sobreescrito cuando se consulta después — comprobado con un diagnóstico ad hoc
/// que aisló el síntoma (login siempre 401 porque <c>FlitDbContext</c> seguía apuntando a
/// <c>flit_local</c>) — pero el <c>NpgsqlDataSource</c> que ya quedó registrado en el contenedor de
/// DI se construyó con el valor ORIGINAL. Las variables de entorno con el separador <c>__</c>
/// (convención .NET) SÍ las lee <c>WebApplication.CreateBuilder(args)</c> como fuente de
/// configuración ANTES de esa lectura, así que sí llegan a tiempo.
/// </para>
/// <para>
/// <b>Se fijan UNA sola vez por proceso, nunca se restauran</b> (a diferencia del patrón habitual de
/// "set en el constructor / restore en Dispose"): con <c>N</c> pruebas HTTP en la colección Postgres
/// (serie, un <see cref="MarcaBlancaApiFactory"/> nuevo por prueba), restaurar en
/// <c>DisposeAsync</c> de la prueba <c>K</c> corre en una tarea async que puede completarse DESPUÉS
/// de que el constructor de la prueba <c>K+1</c> ya puso sus propios valores — se observó
/// exactamente esa carrera (login intermitente 401 solo dentro de la suite completa, nunca en
/// aislamiento) al alternar «set en ctor / restore en Dispose». Como las <c>N</c> pruebas de esta
/// colección quieren SIEMPRE los mismos valores (la misma base efímera, el mismo límite de tasa
/// ampliado, el mismo SMTP desactivado), fijarlos una vez es suficiente y elimina la carrera; el
/// proceso de pruebas termina al cerrar la suite, así que no hay fuga real hacia otro código.
/// </para>
/// </summary>
public sealed class MarcaBlancaApiFactory : WebApplicationFactory<Program>
{
    private static readonly object EnvironmentLock = new();
    private static bool _environmentConfigured;

    public MarcaBlancaApiFactory(PostgresDatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        lock (EnvironmentLock)
        {
            // Idempotente: la cadena de conexión es la MISMA base efímera durante toda la colección
            // (un solo PostgresDatabaseFixture, ICollectionFixture), así que sobreescribir de nuevo
            // en cada prueba es innecesario y es precisamente la fuente de la carrera descrita arriba.
            if (_environmentConfigured)
            {
                return;
            }

            Environment.SetEnvironmentVariable("ConnectionStrings__Core", fixture.ConnectionString);
            // Ya migrada por el fixture: evita reintentar migraciones (y el dev-seeder) en cada arranque.
            Environment.SetEnvironmentVariable("Database__AutoMigrate", "false");
            // Esta suite mide TIEMPO con muestras repetidas (AC2/AC3): un 429 a mitad de medición
            // invalidaría la comparación, no la lógica de anti-enumeración que se está probando.
            Environment.SetEnvironmentVariable("PublicBranding__RateLimit__PermitLimit", "10000");
            Environment.SetEnvironmentVariable("PublicBranding__RateLimit__Window", "00:01:00");
            // Smtp:Host vacío en Development ⇒ InfrastructureExtensions usa ConsoleEmailSender (nunca
            // golpea smtp.office365.com de appsettings.Development.json): la suite verifica el correo
            // vía admin.notification_delivery_logs (AC5/AC6), no el envío real.
            Environment.SetEnvironmentVariable("Smtp__Host", string.Empty);

            _environmentConfigured = true;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Redundante con las variables de entorno de arriba (documentado por si alguna lectura de
        // Program.cs cambiara a IOptions perezoso en el futuro), pero NO sustituye a las env vars
        // para el valor que Program.cs captura en una variable local antes de Build().
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBranding:RateLimit:PermitLimit"] = "10000",
                ["PublicBranding:RateLimit:Window"] = "00:01:00",
            });
        });
    }
}
