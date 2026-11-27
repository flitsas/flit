using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Usuarios;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.Extensions.Primitives;

namespace Flit.Impuestos.Api.Modulos.Backoffice;

/// <summary><c>AprovisionamientoResumen</c>.</summary>
public sealed record AprovisionamientoResumenDto(string Estado, string? Motivo, string? Accion, bool FirmaConfirmada);

/// <summary><c>EmpresaBackoffice</c>. <see cref="Usuarios"/> no se envía si la plataforma no dio el conteo (nunca un 0 inventado).</summary>
public sealed record EmpresaBackofficeDto(
    Guid Id,
    string Nombre,
    string? Nit,
    AprovisionamientoResumenDto Aprovisionamiento,
    bool Suspendida,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Usuarios);

/// <summary><c>PaginaEmpresasBackoffice</c>.</summary>
public sealed record PaginaEmpresasBackofficeDto(IReadOnlyList<EmpresaBackofficeDto> Items, int Total, int Page, int PageSize);

/// <summary>Una transición del historial (<c>AprovisionamientoDetalle.historial</c>).</summary>
public sealed record TransicionAprovisionamientoDto(string? Desde, string Hacia, string? Motivo, string ActorTipo, DateTimeOffset En);

/// <summary><c>AprovisionamientoDetalle</c> (con <c>empresa</c>, delta 7 de la 1.1.0).</summary>
public sealed record AprovisionamientoDetalleDto(
    string Estado,
    string? Motivo,
    string? Accion,
    bool FirmaConfirmada,
    EmpresaIdentificadaDto Empresa,
    bool ModoRespaldoFirma,
    DateTimeOffset ActualizadoEn,
    IReadOnlyList<TransicionAprovisionamientoDto> Historial);

/// <summary>
/// Backoffice de compañías del Super Admin (plan §5.1, F4-CF10; deltas 6 y 7 de impuestos.v1.yaml 1.1.0), bajo
/// <see cref="PoliticaBackoffice"/> y <c>impuestos.backoffice.ver</c>:
/// <list type="bullet">
///   <item><c>GET /backoffice/empresas</c> (<c>q</c> ≤ 100 sobre nombre o NIT, sin tildes ni mayúsculas; <c>page</c>, <c>pageSize</c>
///   1..200): toda compañía que tiene o tuvo Impuestos, incluidas las suspendidas, con su aprovisionamiento, la acción del Super
///   Admin (tabla del plan §3.1) y la cantidad de usuarios de Impuestos que da la plataforma. Cada compañía devuelta deja su
///   lectura auditada.</item>
///   <item><c>GET /backoffice/empresas/{tenantId}/aprovisionamiento</c>: estado, historial (el más reciente primero) y la
///   <c>empresa{id, nombre, nit}</c>; 404 <c>EMPRESA_NO_ENCONTRADA</c> si la compañía no tiene Impuestos. Auditado.</item>
/// </list>
/// Nada de esto lleva api_key ni webhook_secret. El reintento del aprovisionamiento es de M07.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddBackofficeImpuestos(builder.Configuration);
/// EmpresasBackofficeEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class EmpresasBackofficeEndpoints
{
    public const string Ruta = "/backoffice/empresas";
    public const string RecursoLista = "empresas";
    public const string RecursoAprovisionamiento = "aprovisionamiento";

    /// <summary>Motivos del enum <c>MotivoAprovisionamiento</c> del contrato; un código fuera de él no se expone (null).</summary>
    internal static readonly IReadOnlySet<string> MotivosDelContrato = new HashSet<string>(StringComparer.Ordinal)
    {
        "REINTENTO_PROGRAMADO", "SIN_WEBHOOK_SECRET", "ALTA_DATOS_RECHAZADOS", "RAZON_SOCIAL_LARGA", "AMBIENTE_NO_CONFIGURADO",
        "PROVEEDOR_INALCANZABLE", "ALTA_SIN_CONFIRMAR", "EMPRESA_YA_REGISTRADA", "CREDENCIAL_RECHAZADA", "NIT_AUSENTE", "NIT_AMBIGUO",
        "NIT_IGUAL_A_CABEZA", "PRODUCTO_DESACTIVADO",
    };

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var grupo = api.MapGroup(Ruta).RequireAuthorization(PoliticaBackoffice.Nombre).SinCacheEnRespuesta();
        grupo.MapGet(string.Empty, ListarAsync).RequirePermiso(Permisos.BackofficeVer).WithName("ListarEmpresasBackofficeImpuestos");
        grupo.MapGet("/{tenantId}/aprovisionamiento", DetalleAsync).RequirePermiso(Permisos.BackofficeVer).ConEmpresaExplicita(RecursoAprovisionamiento)
            .WithName("ObtenerAprovisionamientoBackofficeImpuestos");
    }

    /// <summary>La acción del Super Admin para el estado (plan §3.1); null si no hay ninguna.</summary>
    internal static string? Accion(EstadoAprovisionamiento estado) => estado switch
    {
        // M07b (1.2.0): el alta idempotente trae el secreto, así que LISTO_SIN_WEBHOOK también se reintenta.
        EstadoAprovisionamiento.ListoSinWebhook or EstadoAprovisionamiento.Error or EstadoAprovisionamiento.Incierto => "REINTENTAR",
        EstadoAprovisionamiento.RequiereCredenciales => "CARGAR_CREDENCIALES",
        EstadoAprovisionamiento.RequiereRevisionNit => "CORREGIR_NIT",
        _ => null,
    };

    internal static string? Motivo(string? codigo) => codigo is not null && MotivosDelContrato.Contains(codigo) ? codigo : null;

    private static async Task<IResult> ListarAsync(
        HttpContext http, LecturaEntreEmpresas lectura, AmbitoDeLaEmpresa ambito, IUsuariosPlataforma usuarios, UsuarioActual usuario,
        AuditoriaLecturaBackoffice auditoria, ILogger<LecturaEntreEmpresas> logger, CancellationToken ct)
    {
        var consulta = http.Request.Query;
        var q = StringValues.IsNullOrEmpty(consulta["q"]) ? null : consulta["q"].ToString();
        if (consulta["q"].Count > 1 || q is { Length: > UsuariosEndpoints.LargoBusqueda })
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.FiltroInvalido);
        var page = Entero(consulta["page"], 1, int.MaxValue, 1);
        var pageSize = Entero(consulta["pageSize"], UsuariosEndpoints.TamanoPorDefecto, UsuariosEndpoints.TamanoMaximo, 1);

        var filas = await lectura.AprovisionamientosAsync(ct).ConfigureAwait(false);
        var directorio = await ambito.DirectorioAsync(ct).ConfigureAwait(false);
        var conteos = await ConteosAsync(usuarios, usuario.Id, logger, ct).ConfigureAwait(false);
        var buscado = q is null ? null : Normalizar(q);

        var todas = filas
            .Select(f => (Fila: f, Empresa: AmbitoDeLaEmpresa.Identificar(directorio, f.TenantId)))
            .Where(x => buscado is null || Normalizar(x.Empresa.Nombre).Contains(buscado, StringComparison.Ordinal)
                || (x.Empresa.Nit is { } nit && nit.Contains(buscado, StringComparison.Ordinal)))
            .OrderBy(x => Normalizar(x.Empresa.Nombre), StringComparer.Ordinal).ThenBy(x => x.Fila.TenantId)
            .ToList();
        var pagina = todas.Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToList();
        foreach (var x in pagina)
            await auditoria.RegistrarAsync(x.Fila.TenantId, RecursoLista, ct).ConfigureAwait(false);

        var items = pagina.Select(x => new EmpresaBackofficeDto(
                x.Fila.TenantId, x.Empresa.Nombre, x.Empresa.Nit, Resumen(x.Fila), x.Fila.Estado == EstadoAprovisionamiento.Suspendido,
                conteos is null ? null : conteos.GetValueOrDefault(x.Fila.TenantId)))
            .ToList();
        return Results.Ok(new PaginaEmpresasBackofficeDto(items, todas.Count, page, pageSize));
    }

    private static async Task<IResult> DetalleAsync(HttpContext http, LecturaEntreEmpresas lectura, AmbitoDeLaEmpresa ambito, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var leido = await lectura.AprovisionamientoAsync(empresa, ct).ConfigureAwait(false) ?? throw ErroresConfiguracion.NoEncontrada();
        var (fila, historial) = leido;
        var resumen = Resumen(fila);
        return Results.Ok(new AprovisionamientoDetalleDto(
            resumen.Estado, resumen.Motivo, resumen.Accion, resumen.FirmaConfirmada,
            await ambito.IdentificarAsync(empresa, ct).ConfigureAwait(false),
            fila.ModoRespaldoFirma, fila.ActualizadoEn,
            [.. historial.Select(h => new TransicionAprovisionamientoDto(
                h.Desde is { } desde ? CodigosDeEnum.Codigo(desde) : null, CodigosDeEnum.Codigo(h.Hacia), Motivo(h.MotivoCodigo), h.ActorTipo, h.En))]));
    }

    private static AprovisionamientoResumenDto Resumen(AprovisionamientoEmpresa fila) =>
        new(CodigosDeEnum.Codigo(fila.Estado), Motivo(fila.MotivoCodigo), Accion(fila.Estado), fila.FirmaConfirmada);

    /// <summary>Usuarios por compañía según la plataforma; null si no responde (la lista sale sin el campo).</summary>
    private static async Task<Dictionary<Guid, int>?> ConteosAsync(IUsuariosPlataforma usuarios, Guid actor, ILogger logger, CancellationToken ct)
    {
        try
        {
            var conteos = await usuarios.ContarPorEmpresaAsync(actor, ct).ConfigureAwait(false);
            var porEmpresa = new Dictionary<Guid, int>();
            foreach (var c in conteos)
            {
                if (Guid.TryParse(c.EmpresaId, out var id))
                    porEmpresa[id] = Math.Max(0, c.Usuarios);
            }

            return porEmpresa;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            BackofficeLog.SinConteos(logger, ex.GetType().Name);
            return null;
        }
    }

    private static int Entero(StringValues valores, int porDefecto, int maximo, int minimo)
    {
        if (StringValues.IsNullOrEmpty(valores))
            return porDefecto;
        return valores.Count == 1 && int.TryParse(valores.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= minimo && n <= maximo
            ? n
            : throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.PaginacionInvalida);
    }

    /// <summary>
    /// Minúsculas y sin tildes, para buscar y ordenar por nombre. Tabla propia de vocales con tilde, diéresis o acento grave y la
    /// ñ: no depende de la normalización Unicode, que el servicio puede correr sin ICU (modo invariante).
    /// </summary>
    internal static string Normalizar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Trim().ToLowerInvariant())
        {
            sb.Append(c switch
            {
                'á' or 'à' or 'ä' or 'â' => 'a',
                'é' or 'è' or 'ë' or 'ê' => 'e',
                'í' or 'ì' or 'ï' or 'î' => 'i',
                'ó' or 'ò' or 'ö' or 'ô' => 'o',
                'ú' or 'ù' or 'ü' or 'û' => 'u',
                'ñ' => 'n',
                _ => c,
            });
        }

        return sb.ToString();
    }
}

internal static partial class BackofficeLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer de la plataforma el conteo de usuarios por compañía ({Motivo}): la lista sale sin él.")]
    public static partial void SinConteos(ILogger logger, string motivo);
}
