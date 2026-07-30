using Flit.DataMigration.Api;
using Flit.DataMigration.Api.Endpoints;
using Flit.DataMigration.Api.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Las variables desplegadas en el .env de la VPS llevan el prefijo FLITMIG_ y las consume tal cual
// el migrador de consola. Añadirlo aquí es lo que permite que los dos servicios del compose
// compartan el MISMO bloque de configuración y no puedan divergir.
builder.Configuration.AddEnvironmentVariables("FLITMIG_");

builder.Services.Configure<MigracionApiOptions>(
    builder.Configuration.GetSection(MigracionApiOptions.SectionName));

var arranque = LoggerFactory
    .Create(b => b.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole())
    .CreateLogger("Migracion");

// El motor solo se arma si el ambiente encendió el endpoint. No es una optimización: desde que este
// servicio dejó el perfil `migracion` sube en TODOS los despliegues, y el motor exige que el .env
// traiga el bloque FLITMIG_ completo (V1Source, los dos file-managers…). Sin esta condición, un
// ambiente que nunca configuró el migrador vería el contenedor reiniciándose en bucle por una
// herramienta que ni piensa usar. Apagado = un proceso vivo que solo responde /health.
var apagable = builder.Configuration
    .GetSection(MigracionApiOptions.SectionName).Get<MigracionApiOptions>() ?? new MigracionApiOptions();

if (apagable.Enabled)
{
    builder.Services.AddMigracionEngine(builder.Configuration, arranque);
}

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<MigracionApiOptions>>().Value;

// Liveness. Fuera del grupo: no exige llave, para que el healthcheck del compose funcione.
app.MapGet("/health", () => Results.Ok(new { status = "alive" }));

// Si el ambiente no ha optado por encender esto, las rutas NI SE REGISTRAN: responden 404, no 403.
// Un 403 confirmaría que el endpoint existe; un 404 no da ni esa señal. Y sobre todo: es lo que
// evita que el disparador de migración sobreviva a la migración sin que nadie tenga que acordarse
// de quitar código.
if (options.Enabled)
{
    MigracionLog.ApiActiva(app.Logger);
    app.MapMigracionEndpoints();
}

await app.RunAsync();

/// <summary>Punto de entrada expuesto para pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
