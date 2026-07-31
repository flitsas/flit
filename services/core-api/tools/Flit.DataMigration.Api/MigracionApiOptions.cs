namespace Flit.DataMigration.Api;

/// <summary>
/// Configuración del host HTTP de migración. Sección <c>MigracionApi</c>, distinta de la sección
/// <c>Migration</c> del motor (<c>BatchId</c>, <c>SystemUserEmail</c>…) para que no se confundan.
/// <para>
/// Se alimenta de las mismas variables <c>FLITMIG_*</c> del <c>.env</c> de la VPS:
/// <c>FLITMIG_MigracionApi__Enabled</c>, <c>FLITMIG_MigracionApi__ApiKey</c>.
/// </para>
/// </summary>
public sealed class MigracionApiOptions
{
    public const string SectionName = "MigracionApi";

    /// <summary>
    /// Apagado por defecto, y esto es deliberado: si está en <c>false</c> las rutas ni siquiera se
    /// registran, así que responden 404 y no hay superficie que sondear. Se enciende en el
    /// <c>.env</c> mientras dura una ola y se apaga después, sin desplegar nada.
    /// <para>
    /// Es lo que evita que este endpoint sobreviva a la migración: no depende de que alguien se
    /// acuerde de quitar código.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Llave compartida que exige la cabecera <c>X-Migration-Key</c>. Si está vacía el host
    /// arranca pero NO valida nada (fail-closed): ver <see cref="Authorization.MigracionApiKey"/>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Migraciones simultáneas. Dos por defecto: la instancia 3 admite respuestas de hasta 256 MB,
    /// y cuatro peticiones grandes a la vez tumbarían el contenedor. El Runner de Postman es
    /// secuencial, así que en el uso normal ni se nota.
    /// </summary>
    public int MaxConcurrentRuns { get; set; } = 2;

    /// <summary>
    /// Comprobar al arrancar que la base responde y que el entorno destino está listo.
    /// <para>
    /// Se puede apagar para los tests, que levantan el host sin Postgres accesible — el mismo
    /// truco que <c>Database__AutoMigrate=false</c> en los tests de Flit.Api.
    /// </para>
    /// </summary>
    public bool RunSchemaCheckAtStartup { get; set; } = true;
}
