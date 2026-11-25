using System.Text.Json;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Impuestos.Api.Modulos.Catalogos;

/// <summary>
/// Caché global de <c>GET /api/v1/fuentes</c> del proveedor en <c>impuestos.fuentes_api</c> (plan §4.1 y §5.1, API §4): un
/// departamento por fila con su nombre, si está activo y su <c>params_schema</c> (lo usa el constructor de la solicitud, M09).
/// <see cref="ObtenerAsync(Guid, CancellationToken)"/> exige la compañía en <c>LISTO</c> o <c>LISTO_SIN_WEBHOOK</c> (si no, 409
/// <c>APROVISIONAMIENTO_NO_LISTO</c> sin llamar al API) y devuelve la caché si está vigente (<see cref="ClaveVigencia"/>, 6 horas
/// por defecto, contada desde la fila más vieja). Si no, la refresca con la api_key de la compañía: crea, actualiza y retira lo
/// que el proveedor ya no trae. Si el proveedor no responde, 503 <c>PROVEEDOR_NO_DISPONIBLE</c>. Códigos de departamento: se
/// recortan y una cifra se completa con 0; los que no quedan en 2 cifras o se repiten no entran. Sin <c>name</c> se usa
/// <c>department_name</c>; sin <c>active</c> el departamento queda inactivo (no se ofrece). Una lista vacía no borra la caché.
/// </summary>
/// <example>
/// <code>
/// var fuentes = await sincronizacion.ObtenerAsync(empresa, ct); // [{ DepartamentoCodigo, Nombre, Activa, ParamsSchema }]
/// var disponibles = await SincronizacionFuentes.CacheAsync(db, ct); // sin refrescar
/// </code>
/// </example>
public sealed partial class SincronizacionFuentes(
    AlmacenCredenciales almacen, ClienteApiLiquidacion api, TimeProvider reloj, IConfiguration configuracion, ILogger<SincronizacionFuentes> log)
{
    public const string ClaveVigencia = "Impuestos:Fuentes:VigenciaCache";

    public static readonly TimeSpan VigenciaPorDefecto = TimeSpan.FromHours(6);

    private const int LargoNombre = 80;

    /// <summary>Estados del aprovisionamiento en los que la compañía puede usar el API (y por tanto <c>/fuentes</c>).</summary>
    public static bool EstaLista(EstadoAprovisionamiento? estado) => estado is EstadoAprovisionamiento.Listo or EstadoAprovisionamiento.ListoSinWebhook;

    /// <summary>Las fuentes vigentes para la compañía (ver la clase): 409 si no está lista, 503 si el proveedor no responde.</summary>
    public async Task<IReadOnlyList<FuenteApi>> ObtenerAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var estado = await EstadoAsync(db, empresa, ct).ConfigureAwait(false);
        if (!EstaLista(estado))
            throw ErroresConfiguracion.NoListo();
        return await ObtenerAsync(db, empresa, ct).ConfigureAwait(false);
    }

    /// <summary>Las fuentes vigentes con el contexto de la compañía; quien llama ya comprobó que está lista.</summary>
    internal async Task<IReadOnlyList<FuenteApi>> ObtenerAsync(ImpuestosDb db, Guid empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ahora = reloj.GetUtcNow();
        var cache = await CacheAsync(db, ct).ConfigureAwait(false);
        if (cache.Count > 0 && cache.Min(f => f.SincronizadoEn) > ahora - Vigencia())
            return cache;

        var credenciales = await almacen.LeerAsync(db, empresa, ct).ConfigureAwait(false)
            ?? throw ErroresConfiguracion.NoListo();
        var respuesta = await api.ObtenerFuentesAsync(credenciales.ApiKey, ct).ConfigureAwait(false);
        if (!respuesta.Exitoso)
        {
            NoDisponible(log, empresa, respuesta.Clase, respuesta.Status, respuesta.CodigoError);
            throw ErroresConfiguracion.SinProveedor();
        }

        return await AplicarAsync(db, respuesta.Valor!.Items, ahora, ct).ConfigureAwait(false);
    }

    /// <summary>La caché tal como está, ordenada por código, sin llamar al API.</summary>
    public static async Task<List<FuenteApi>> CacheAsync(ImpuestosDb db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Set<FuenteApi>().AsNoTracking().OrderBy(f => f.DepartamentoCodigo).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>El estado del aprovisionamiento de la compañía; null si no tiene fila (no tiene Impuestos).</summary>
    internal static Task<EstadoAprovisionamiento?> EstadoAsync(ImpuestosDb db, Guid empresa, CancellationToken ct) =>
        db.Set<AprovisionamientoEmpresa>().AsNoTracking().Where(a => a.TenantId == empresa)
            .Select(a => (EstadoAprovisionamiento?)a.Estado).FirstOrDefaultAsync(ct);

    private TimeSpan Vigencia()
    {
        var vigencia = configuracion.GetValue(ClaveVigencia, VigenciaPorDefecto);
        return vigencia > TimeSpan.Zero ? vigencia : VigenciaPorDefecto;
    }

    private async Task<IReadOnlyList<FuenteApi>> AplicarAsync(ImpuestosDb db, IReadOnlyList<FuenteDelApi>? items, DateTimeOffset ahora, CancellationToken ct)
    {
        var nuevas = new Dictionary<string, FuenteApi>(StringComparer.Ordinal);
        foreach (var item in items ?? [])
        {
            var codigo = SincronizacionOrganismos.CodigoDepartamento(item.DepartmentCode);
            if (codigo is null || nuevas.ContainsKey(codigo))
                continue;
            var nombre = (string.IsNullOrWhiteSpace(item.Name) ? item.DepartmentName : item.Name)?.Trim();
            nuevas[codigo] = new FuenteApi
            {
                DepartamentoCodigo = codigo,
                Nombre = string.IsNullOrEmpty(nombre) ? codigo : nombre.Length > LargoNombre ? nombre[..LargoNombre] : nombre,
                Activa = item.Active ?? false,
                ParamsSchema = item.ParamsSchema is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } esquema ? esquema.GetRawText() : "{}",
                SincronizadoEn = ahora,
            };
        }

        if (nuevas.Count == 0)
        {
            ListaVacia(log);
            return await CacheAsync(db, ct).ConfigureAwait(false);
        }

        var locales = await db.Set<FuenteApi>().ToListAsync(ct).ConfigureAwait(false);
        foreach (var local in locales)
        {
            if (!nuevas.Remove(local.DepartamentoCodigo, out var nueva))
            {
                db.Remove(local);
                continue;
            }

            local.Nombre = nueva.Nombre;
            local.Activa = nueva.Activa;
            local.ParamsSchema = nueva.ParamsSchema;
            local.SincronizadoEn = ahora;
        }

        db.AddRange(nuevas.Values);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Otra petición refrescó la caché al mismo tiempo: vale la que quedó guardada.
            db.ChangeTracker.Clear();
        }

        var resultado = await CacheAsync(db, ct).ConfigureAwait(false);
        Refrescada(log, resultado.Count);
        return resultado;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo refrescar /fuentes con la credencial de la empresa {Empresa} ({Clase}, estado {Status}, código {Codigo}).")]
    private static partial void NoDisponible(ILogger logger, Guid empresa, ClaseEnvio clase, int? status, string? codigo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El proveedor devolvió /fuentes sin departamentos válidos: la caché no se toca.")]
    private static partial void ListaVacia(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Caché de /fuentes refrescada: {Total} departamentos.")]
    private static partial void Refrescada(ILogger logger, int total);
}
