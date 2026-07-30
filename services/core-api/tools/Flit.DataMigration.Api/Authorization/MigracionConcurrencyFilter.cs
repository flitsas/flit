using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Flit.DataMigration.Api.Authorization;

/// <summary>
/// Tope global de migraciones simultáneas.
/// <para>
/// No es prudencia genérica: el cliente de la instancia 3 admite respuestas de hasta 256 MB
/// porque los expedientes consolidados rondan los 9-12 MB y V1 los arma al vuelo. Cuatro
/// peticiones grandes a la vez dejan sin memoria a un contenedor pequeño, y el fallo aparecería
/// como un trámite a medio migrar, no como un error limpio.
/// </para>
/// <para>
/// Devuelve 429 en vez de encolar: el Runner de Postman reintenta, y encolar peticiones que
/// duran minutos solo mueve el problema al timeout del cliente.
/// </para>
/// </summary>
internal sealed class MigracionConcurrencyFilter : IEndpointFilter, IDisposable
{
    private readonly SemaphoreSlim slots;

    public MigracionConcurrencyFilter(IOptions<MigracionApiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var max = Math.Max(1, options.Value.MaxConcurrentRuns);
        slots = new SemaphoreSlim(max, max);
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!await slots.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "migracion.ocupado",
                detail: "Hay demasiadas migraciones en curso. Reintenta cuando terminen; el migrador es idempotente.");
        }

        try
        {
            return await next(context).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    public void Dispose() => slots.Dispose();
}
