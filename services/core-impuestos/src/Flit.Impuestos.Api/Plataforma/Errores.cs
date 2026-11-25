using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Regla de negocio incumplida: la API responde <c>{ code, error }</c> con el estado indicado. El código es estable (el front y
/// las pruebas lo leen); el mensaje es para el usuario y no repite valores de la petición.
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

    /// <summary>Campo de la petición que incumple la regla; null si no es de un campo.</summary>
    public string? Campo { get; init; }

    /// <summary>Segundos de <c>Retry-After</c>; null si no aplica.</summary>
    public int? RetryAfterSegundos { get; init; }

    public static ReglaException NoEncontrado(string que) => new("NO_ENCONTRADO", $"{que} no existe o no es de tu empresa.", StatusCodes.Status404NotFound);

    public static ReglaException Conflicto(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status409Conflict);

    public static ReglaException Prohibido(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status403Forbidden);
}

/// <summary>
/// Convierte las excepciones de negocio en <c>{ code, error }</c>: <see cref="ReglaException"/> con su estado,
/// <see cref="TransicionInvalida"/> del dominio como 409 con su código, una escritura entre empresas como 403, un CHECK de la base
/// como 422 <c>VALOR_INVALIDO</c> (sin el detalle de la base) y un conflicto de <c>xmin</c> como 409. Lo demás sigue su curso.
/// </summary>
internal sealed class ManejadorReglas : IExceptionHandler
{
    public const string CodigoValorInvalido = "VALOR_INVALIDO";
    public const string CodigoConflictoConcurrencia = "CONFLICTO_CONCURRENCIA";
    public const string CodigoEntreEmpresas = "ESCRITURA_ENTRE_EMPRESAS";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var regla = exception switch
        {
            ReglaException r => r,
            TransicionInvalida t => ReglaException.Conflicto(t.Codigo, t.Message),
            EscrituraEntreEmpresasException => ReglaException.Prohibido(CodigoEntreEmpresas, "No se pueden escribir datos de otra empresa."),
            DbUpdateConcurrencyException =>
                ReglaException.Conflicto(CodigoConflictoConcurrencia, "Otro cambio se guardó al mismo tiempo. Vuelve a leer y reintenta."),
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
