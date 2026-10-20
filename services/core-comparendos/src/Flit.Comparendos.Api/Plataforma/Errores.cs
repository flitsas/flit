using Microsoft.AspNetCore.Diagnostics;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>
/// Regla de negocio incumplida: la API responde <c>{ code, error }</c> con el estado indicado. El código es estable
/// (la app y las pruebas lo leen); el mensaje es para el usuario.
/// </summary>
public sealed class ReglaException : Exception
{
    public ReglaException()
    {
        Codigo = "REGLA";
    }

    public ReglaException(string message)
        : base(message)
    {
        Codigo = "REGLA";
    }

    public ReglaException(string message, Exception innerException)
        : base(message, innerException)
    {
        Codigo = "REGLA";
    }

    public ReglaException(string codigo, string mensaje, int status = StatusCodes.Status422UnprocessableEntity)
        : base(mensaje)
    {
        Codigo = codigo;
        Status = status;
    }

    public string Codigo { get; }
    public int Status { get; } = StatusCodes.Status422UnprocessableEntity;

    public static ReglaException NoEncontrado(string que) => new("NO_ENCONTRADO", $"{que} no existe o no es de tu empresa.", StatusCodes.Status404NotFound);

    public static ReglaException Conflicto(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status409Conflict);

    public static ReglaException Prohibido(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status403Forbidden);
}

/// <summary>Convierte <see cref="ReglaException"/> en la respuesta JSON <c>{ code, error }</c>.</summary>
internal sealed class ManejadorReglas : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ReglaException regla)
            return false;

        httpContext.Response.StatusCode = regla.Status;
        await httpContext.Response.WriteAsJsonAsync(new { code = regla.Codigo, error = regla.Message }, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
