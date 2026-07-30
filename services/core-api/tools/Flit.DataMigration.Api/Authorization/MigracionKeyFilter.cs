using Microsoft.AspNetCore.Http;

namespace Flit.DataMigration.Api.Authorization;

/// <summary>
/// Exige la cabecera <c>X-Migration-Key</c> en todo el grupo de migración.
/// <para>
/// Es un <see cref="IEndpointFilter"/> y no un esquema de autenticación con su policy porque un
/// <c>AuthenticationScheme</c> aporta claims, negociación entre esquemas y composición con
/// <c>[Authorize]</c> — y aquí no hace falta ninguna de las tres: hay un solo llamador, una sola
/// cadena y cero claims. Montarlo por esquema exigiría JwtBearer o un <c>AuthenticationHandler</c>
/// a mano, más <c>UseAuthentication</c>, <c>UseAuthorization</c> y una policy: cinco piezas para
/// comparar un string. El filtro son veinte líneas y un único punto de enganche.
/// </para>
/// <para>
/// Se aplica al GRUPO, no a cada ruta, para que ninguna ruta futura pueda olvidarlo.
/// </para>
/// </summary>
internal sealed class MigracionKeyFilter(MigracionApiKey key) : IEndpointFilter
{
    internal const string HeaderName = "X-Migration-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (!key.Matches(provided))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "migracion.llave_invalida",
                detail: key.NotConfigured
                    // Un 401 permanente sin pista es un rato perdido en la VPS. Decir que falta la
                    // configuración no filtra nada: quien pregunta ya no tiene acceso.
                    ? "El host arrancó sin FLITMIG_MigracionApi__ApiKey configurada, así que ninguna llave es válida."
                    : "Falta la cabecera X-Migration-Key o su valor no coincide.");
        }

        return await next(context).ConfigureAwait(false);
    }
}
