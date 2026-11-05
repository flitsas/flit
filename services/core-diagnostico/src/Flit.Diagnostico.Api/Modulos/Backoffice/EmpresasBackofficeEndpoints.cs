using System.Text;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Plataforma;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// <c>GET /backoffice/empresas</c> (HU #13741, escenarios 15 y 16): las empresas activas con Diagnóstico encendido
/// (<c>[{ id, nombre, nit }]</c>, ordenadas por nombre) para el selector del editor de contrato. Las da Identidad
/// (<see cref="IEmpresasPlataforma"/>); Diagnóstico no administra compañías y la ruta es de solo lectura. Misma
/// autorización que el contrato: política <see cref="PoliticaBackoffice"/> (<c>tenant_type=FLIT</c> +
/// <c>diagnostico.backoffice.ver</c>) y <c>diagnostico.configuracion.contrato.editar</c>. No lleva empresa explícita: no
/// lee datos de ninguna compañía, solo la lista de la plataforma. Sin Identidad configurada: 503
/// <see cref="CodigoNoConfigurada"/>; Identidad no responde: 503 <see cref="CodigoNoDisponible"/> con <c>Retry-After</c>.
/// Nunca una lista vacía que parezca real.
/// </summary>
internal static partial class EmpresasBackofficeEndpoints
{
    public const string Ruta = "/empresas";
    public const string CodigoNoConfigurada = "IDENTIDAD_NO_CONFIGURADA";
    public const string CodigoNoDisponible = "IDENTIDAD_NO_DISPONIBLE";

    /// <summary>La ruta va en un grupo <c>/backoffice</c> con la política, pero sin el filtro de empresa explícita.</summary>
    public static void Map(RouteGroupBuilder backofficeSinEmpresa)
    {
        backofficeSinEmpresa.MapGet(Ruta, Listar).RequirePermiso(Permisos.ConfiguracionContratoEditar).WithName("ListarEmpresasBackoffice");
    }

    private static async Task<IResult> Listar(IEmpresasPlataforma plataforma, ILoggerFactory logs, CancellationToken ct)
    {
        IReadOnlyList<EmpresaPlataforma>? empresas;
        try
        {
            empresas = await plataforma.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (EsFallaDeIdentidad(ex) && !ct.IsCancellationRequested)
        {
            // Solo transporte/proveedor: el motivo va al log (sin nombres ni NIT); al cliente, un 503 explícito. Cualquier otra
            // excepción (error de programación, DI) sigue por el pipeline normal como 500 con su log completo, y la
            // cancelación del cliente se propaga.
            LogNoDisponible(logs.CreateLogger(typeof(EmpresasBackofficeEndpoints)), ex is RpcException rpc ? rpc.StatusCode.ToString() : ex.GetType().Name);
            throw new ReglaException(CodigoNoDisponible, "No se pudo leer la lista de empresas de Identidad. Intenta de nuevo en unos segundos.",
                StatusCodes.Status503ServiceUnavailable)
            { RetryAfterSegundos = 30 };
        }

        if (empresas is null)
        {
            throw new ReglaException(CodigoNoConfigurada, "La lista de empresas no está disponible: el servicio no tiene configurada la conexión con Identidad.",
                StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(empresas
            .OrderBy(e => ClaveOrden(e.Nombre), StringComparer.Ordinal)
            .ThenBy(e => e.Nombre, StringComparer.Ordinal)
            .ThenBy(e => e.Id)
            .Select(e => new EmpresaBackofficeDto(e.Id, e.Nombre, e.Nit)));
    }

    /// <summary>
    /// Fallas de Identidad que merecen 503: <see cref="RpcException"/> (Identidad caída, plazo vencido, circuito abierto del
    /// SDK y también el token de servicio rechazado o sin respuesta: Grpc.Net.Client envuelve en <see cref="RpcException"/>
    /// lo que lanza el <c>ServiceTokenProvider</c> del SDK dentro de las credenciales de la llamada) y
    /// <see cref="HttpRequestException"/> (red). Nada más: un <see cref="InvalidOperationException"/> suelto es un error de
    /// programación, no una caída.
    /// </summary>
    internal static bool EsFallaDeIdentidad(Exception ex) => ex is RpcException or HttpRequestException;

    /// <summary>
    /// Clave de orden alfabético estable sin depender de ICU (la imagen alpine corre en modo invariante): mayúsculas y
    /// vocales sin tilde, así «Álamo» va antes que «Zeta». La Ñ queda después de la N.
    /// </summary>
    internal static string ClaveOrden(string nombre)
    {
        var sb = new StringBuilder(nombre.Length);
        foreach (var c in nombre.ToUpperInvariant())
        {
            switch (c)
            {
                case 'Á' or 'À' or 'Ä' or 'Â': sb.Append('A'); break;
                case 'É' or 'È' or 'Ë' or 'Ê': sb.Append('E'); break;
                case 'Í' or 'Ì' or 'Ï' or 'Î': sb.Append('I'); break;
                case 'Ó' or 'Ò' or 'Ö' or 'Ô': sb.Append('O'); break;
                case 'Ú' or 'Ù' or 'Ü' or 'Û': sb.Append('U'); break;
                case 'Ñ': sb.Append('N').Append(char.MaxValue); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer la lista de empresas de Identidad ({Motivo}): el backoffice recibe 503.")]
    private static partial void LogNoDisponible(ILogger logger, string motivo);
}

/// <summary>Una empresa del selector del backoffice: <c>{ id, nombre, nit }</c> (<c>nit</c> null si Identidad no lo informa).</summary>
internal sealed record EmpresaBackofficeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("nit")] string? Nit);
