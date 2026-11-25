using System.Security.Cryptography;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Avisos;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Conciliacion;

/// <summary>Qué hizo la correlación con un ítem <c>INCIERTO</c>.</summary>
public enum ResultadoCorrelacion
{
    /// <summary>El token apareció en el listado: el ítem queda <c>ENVIADO</c> con el id adoptado.</summary>
    Adoptado,

    /// <summary>Sin secreto, sin credencial o sin el token en el listado: <c>INCIERTO_REQUIERE_DECISION</c>.</summary>
    RequiereDecision,

    /// <summary>Todavía no vence la espera.</summary>
    Esperando,

    /// <summary>El listado falló; se reintenta en <see cref="OpcionesConciliacion.ReintentoIncierto"/>.</summary>
    Reintentar,

    /// <summary>El ítem ya no está en <c>INCIERTO</c>.</summary>
    SinCambio,
}

/// <summary>
/// Correlación del «Incierto» (plan §1.5, DA-4, F2a-CF7). Sin secreto (la consulta salió sin <c>callbackUrl</c>) no hay token que
/// buscar: el paso a <c>INCIERTO_REQUIERE_DECISION</c> es directo. Con secreto, pasada <see cref="OpcionesConciliacion.EsperaIncierto"/>
/// desde el envío, recorre <c>GET /api/v1/consultas?limit=&amp;cursor=</c> hasta <see cref="OpcionesConciliacion.PaginasIncierto"/>
/// páginas buscando una consulta cuyo <c>callback_url</c> termine en el token del ítem (se compara su SHA-256 con
/// <c>token_hash</c>, en tiempo constante). Si aparece, adopta su id y queda <c>ENVIADO</c> para consultarla ya; si no, requiere
/// decisión. Nunca empareja por placa ni por hora. El estado y los contadores del lote se guardan con <see cref="AplicadorResultado"/>.
/// </summary>
/// <example>
/// <code>
/// var r = await correlacion.ResolverAsync(empresa, itemId, ct); // ResultadoCorrelacion.Adoptado
/// </code>
/// </example>
public sealed partial class CorrelacionIncierto(
    ImpuestosDb db,
    EmpresaDelTrabajo empresaDelTrabajo,
    AlmacenCredenciales almacen,
    ClienteApiLiquidacion api,
    AplicadorResultado aplicador,
    OpcionesConciliacion opciones,
    TimeProvider reloj,
    ILogger<CorrelacionIncierto> log)
{
    public async Task<ResultadoCorrelacion> ResolverAsync(Guid empresa, Guid itemId, CancellationToken ct)
    {
        empresaDelTrabajo.Fijar(empresa);
        var item = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => new { i.Estado, i.ConCallback, i.TokenHash, i.EnviadoEn, i.ActualizadoEn })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null || item.Estado != EstadoItem.Incierto)
            return ResultadoCorrelacion.SinCambio;
        if (!item.ConCallback || item.TokenHash is null)
            return await DecidirAsync(empresa, itemId, null, ct).ConfigureAwait(false);

        var ahora = reloj.GetUtcNow();
        if (ahora < (item.EnviadoEn ?? item.ActualizadoEn) + opciones.EsperaIncierto)
            return ResultadoCorrelacion.Esperando;

        var credenciales = await almacen.LeerAsync(empresa, ct).ConfigureAwait(false);
        if (credenciales is null)
            return await DecidirAsync(empresa, itemId, null, ct).ConfigureAwait(false);

        string? cursor = null;
        for (var pagina = 0; pagina < opciones.PaginasIncierto; pagina++)
        {
            var r = await api.ListarConsultasAsync(credenciales.ApiKey, null, opciones.LimitePaginaIncierto, cursor, ct).ConfigureAwait(false);
            if (!r.Exitoso)
            {
                ListadoFallido(log, itemId, r.Clase, r.Status);
                await db.Set<LiquidacionItem>().Where(i => i.Id == itemId && i.Estado == EstadoItem.Incierto)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProximoPollEn, ahora + opciones.ReintentoIncierto), ct).ConfigureAwait(false);
                return ResultadoCorrelacion.Reintentar;
            }

            var hallada = r.Valor!.Items.FirstOrDefault(c => EsSuToken(c.CallbackUrl, item.TokenHash));
            if (hallada is not null)
                return await DecidirAsync(empresa, itemId, hallada.Id, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(r.Valor.NextCursor))
                break;
            cursor = r.Valor.NextCursor;
        }

        return await DecidirAsync(empresa, itemId, null, ct).ConfigureAwait(false);
    }

    /// <summary>El último segmento del <c>callback_url</c> es el token del ítem (se compara por su hash).</summary>
    internal static bool EsSuToken(string? callbackUrl, byte[] tokenHash)
    {
        if (string.IsNullOrEmpty(callbackUrl))
            return false;
        var ruta = callbackUrl.Split('?', '#')[0].TrimEnd('/');
        var token = ruta[(ruta.LastIndexOf('/') + 1)..];
        return token.Length > 0 && CryptographicOperations.FixedTimeEquals(ProcesadorAviso.HashDelToken(token), tokenHash);
    }

    private async Task<ResultadoCorrelacion> DecidirAsync(Guid empresa, Guid itemId, string? consultaHallada, CancellationToken ct)
    {
        var r = await aplicador.TransicionarAsync(empresa, itemId, (item, ahora) =>
        {
            if (item.Estado != EstadoItem.Incierto)
                return Aplicacion.SinCambio;
            var t = TransicionesItem.Aplicar(
                new SituacionItem(item.Estado, item.IntentosRunt, item.IntentosReenvio, item.ErrorClase),
                new EventoItem.PlazoInciertoVencido(consultaHallada is not null));
            item.Estado = t.Nueva.Estado;
            item.ActualizadoEn = ahora;
            if (consultaHallada is not null)
            {
                item.ConsultaApiId = consultaHallada;
                item.ProximoPollEn = ahora;
            }
            else
            {
                item.ProximoPollEn = null;
            }

            return Aplicacion.Aplicado;
        }, ct).ConfigureAwait(false);

        if (r != Aplicacion.Aplicado)
            return ResultadoCorrelacion.SinCambio;
        Resuelto(log, itemId, consultaHallada is not null);
        return consultaHallada is null ? ResultadoCorrelacion.RequiereDecision : ResultadoCorrelacion.Adoptado;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ítem incierto {Item}: consulta hallada por su token = {Hallada}.")]
    private static partial void Resuelto(ILogger logger, Guid item, bool hallada);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo listar las consultas para el ítem incierto {Item} ({Clase}, estado {Status}); se reintenta más tarde.")]
    private static partial void ListadoFallido(ILogger logger, Guid item, ClaseEnvio clase, int? status);
}
