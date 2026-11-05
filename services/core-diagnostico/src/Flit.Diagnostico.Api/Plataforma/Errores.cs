using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Regla de negocio incumplida (portado de Comparendos, HU #13724): la API responde <c>{ code, error }</c> con el estado
/// indicado. El código es estable (la app y las pruebas lo leen); el mensaje es para el usuario.
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

    private ReglaException(string codigo, string mensaje, Exception causa)
        : base(mensaje, causa)
    {
        Codigo = codigo;
        Status = StatusCodes.Status503ServiceUnavailable;
    }

    /// <summary>El catálogo de tipos de trámite de Trámites no está disponible (HU #13742): 503 con <c>Retry-After</c>.</summary>
    public const string CodigoCatalogoNoDisponible = "CATALOGO_NO_DISPONIBLE";

    public string Codigo { get; }

    public int Status { get; } = StatusCodes.Status422UnprocessableEntity;

    /// <summary>Campo del cuerpo que incumple la regla (snake_case, HU #13750); null si la regla no es de un campo.</summary>
    public string? Campo { get; init; }

    /// <summary>Segundos de la cabecera <c>Retry-After</c> (solo en un 503 de una dependencia caída); null si no aplica.</summary>
    public int? RetryAfterSegundos { get; init; }

    public static ReglaException NoEncontrado(string que) => new("NO_ENCONTRADO", $"{que} no existe o no es de tu empresa.", StatusCodes.Status404NotFound);

    public static ReglaException Conflicto(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status409Conflict);

    /// <summary>503 <see cref="CodigoCatalogoNoDisponible"/> con <c>Retry-After: 30</c> (HU #13742; F01-H4 reusa la conducta).</summary>
    public static ReglaException CatalogoNoDisponible(Exception causa) =>
        new(CodigoCatalogoNoDisponible, "El catálogo de tipos de trámite no está disponible en este momento. Intenta de nuevo en unos segundos.", causa)
        {
            RetryAfterSegundos = 30,
        };

    public static ReglaException Prohibido(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status403Forbidden);
}

/// <summary>
/// Convierte <see cref="ReglaException"/> en la respuesta JSON <c>{ code, error }</c>. Una violación de CHECK de la base
/// (SQLSTATE 23514) que se escape a la validación de la aplicación también es una regla de dominio: 422
/// <c>VALOR_INVALIDO</c>, sin el nombre del CHECK ni el detalle de la base (HU #13741). Si la regla trae
/// <see cref="ReglaException.RetryAfterSegundos"/>, la respuesta lleva <c>Retry-After</c> (HU #13742). Cualquier otro error
/// sigue su curso. Si la regla nombra el campo (<see cref="ReglaException.Campo"/>), el cuerpo lo trae en <c>campo</c>.
/// </summary>
internal sealed class ManejadorReglas : IExceptionHandler
{
    public const string CodigoValorInvalido = "VALOR_INVALIDO";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var regla = exception switch
        {
            ReglaException r => r,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation } } =>
                new ReglaException(CodigoValorInvalido, "Los datos no cumplen una regla de validación del servicio."),
            _ => null,
        };
        if (regla is null)
            return false;

        httpContext.Response.StatusCode = regla.Status;
        if (regla.RetryAfterSegundos is { } segundos)
            httpContext.Response.Headers.RetryAfter = segundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        object cuerpo = regla.Campo is null
            ? new { code = regla.Codigo, error = regla.Message }
            : new { code = regla.Codigo, error = regla.Message, campo = regla.Campo };
        await httpContext.Response.WriteAsJsonAsync(cuerpo, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
