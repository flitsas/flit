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

    /// <summary>Tope de la regla (<c>LIMITE_SUPERADO</c>, <c>TOPE_SUPERA_CONTRACTUAL</c>, <c>TOPE_EXPORTACION</c>); null si no aplica.</summary>
    public int? Tope { get; init; }

    /// <summary>Segundos de <c>Retry-After</c>; null si no aplica.</summary>
    public int? RetryAfterSegundos { get; init; }

    public static ReglaException NoEncontrado(string que) => new("NO_ENCONTRADO", $"{que} no existe o no es de tu empresa.", StatusCodes.Status404NotFound);

    public static ReglaException Conflicto(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status409Conflict);

    public static ReglaException Prohibido(string codigo, string mensaje) => new(codigo, mensaje, StatusCodes.Status403Forbidden);
}

/// <summary>
/// Convierte las excepciones de negocio en <c>{ code, error }</c>: <see cref="ReglaException"/> con su estado (y
/// <c>campo</c> y <c>tope</c> si los trae), <see cref="TransicionInvalida"/> del dominio como 409 con su código, una escritura
/// entre empresas como 403, un CHECK de la base como 422 <c>VALOR_INVALIDO</c> (sin el detalle de la base), un conflicto de
/// <c>xmin</c> como 409 y un permiso negado por la plataforma sin código como 403 sin cuerpo. Lo demás sigue su curso.
/// </summary>
internal sealed class ManejadorReglas : IExceptionHandler
{
    public const string CodigoValorInvalido = "VALOR_INVALIDO";
    public const string CodigoConflictoConcurrencia = "CONFLICTO_CONCURRENCIA";
    public const string CodigoEntreEmpresas = "ESCRITURA_ENTRE_EMPRESAS";
    public const string CodigoSinUsuario = "SIN_USUARIO";

    /// <summary>Literal de <c>VALOR_INVALIDO</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.2.2).</summary>
    public const string LiteralValorInvalido = "Un valor enviado no es válido. Revisa los datos e inténtalo de nuevo.";

    /// <summary>Literal de <c>CONFLICTO_CONCURRENCIA</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.2.2).</summary>
    public const string LiteralConflictoConcurrencia = "Otro usuario cambió este registro al mismo tiempo. Recarga e inténtalo de nuevo.";

    /// <summary>Literal de <c>ESCRITURA_ENTRE_EMPRESAS</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.2.2).</summary>
    public const string LiteralEntreEmpresas = "No puedes modificar datos de otra compañía.";

    /// <summary>Literal de <c>SIN_USUARIO</c> en <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.2.2).</summary>
    public const string LiteralSinUsuario = "Tu sesión no identifica a un usuario. Vuelve a iniciar sesión.";

    /// <summary>403 <c>SIN_USUARIO</c> con el literal del contrato: el token no trae un <c>sub</c> uuid.</summary>
    public static ReglaException SinUsuario() => ReglaException.Prohibido(CodigoSinUsuario, LiteralSinUsuario);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is Modulos.Usuarios.PermisoNegadoException)
        {
            // La plataforma negó el permiso sin un código del contrato: 403 sin cuerpo (respuesta Prohibido de impuestos.v1.yaml).
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return true;
        }

        var regla = exception switch
        {
            ReglaException r => r,
            TransicionInvalida t => ReglaException.Conflicto(t.Codigo, t.Message),
            EscrituraEntreEmpresasException => ReglaException.Prohibido(CodigoEntreEmpresas, LiteralEntreEmpresas),
            DbUpdateConcurrencyException =>
                ReglaException.Conflicto(CodigoConflictoConcurrencia, LiteralConflictoConcurrencia),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation } } =>
                new ReglaException(CodigoValorInvalido, LiteralValorInvalido),
            _ => null,
        };
        if (regla is null)
            return false;

        httpContext.Response.StatusCode = regla.Status;
        if (regla.RetryAfterSegundos is { } segundos)
            httpContext.Response.Headers.RetryAfter = segundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cuerpo = new Dictionary<string, object> { ["code"] = regla.Codigo, ["error"] = regla.Message };
        if (regla.Campo is not null)
            cuerpo["campo"] = regla.Campo;
        if (regla.Tope is { } tope)
            cuerpo["tope"] = tope;
        await httpContext.Response.WriteAsJsonAsync(cuerpo, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
