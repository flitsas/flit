using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Notificaciones;

/// <summary>Esquema <c>Notificacion</c>: <see cref="Leida"/> es para el usuario del token.</summary>
public sealed record NotificacionDto(Guid Id, string Tipo, Guid LiquidacionId, DateTimeOffset CreadaEn, bool Leida);

/// <summary>
/// Aviso en pantalla (HU #14694, M12; DA-6b, plan §6.1 «la campana»), con la política de usuario del producto (autenticado):
/// <list type="bullet">
///   <item><c>GET /notificaciones</c>: los avisos de la compañía del token, el más reciente primero (hasta <see cref="Maximo"/>), con
///   <c>leida</c> para el usuario.</item>
///   <item><c>POST /notificaciones/{id}/leida</c>: suma el usuario a <c>leida_por</c>; idempotente (204). Un aviso que no existe o es de
///   otra compañía responde 404 sin cuerpo (el contrato no le da código).</item>
/// </list>
/// Solo lee y escribe con el contexto de la compañía del token: el filtro de empresa aísla.
/// </summary>
/// <example>
/// <code>
/// NotificacionesEndpoints.Map(api);
/// </code>
/// </example>
internal static class NotificacionesEndpoints
{
    public const string Ruta = "/notificaciones";
    public const int Maximo = 50;

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var grupo = api.MapGroup(Ruta).RequireAuthorization(AutorizacionExtensions.PoliticaUsuario);
        grupo.MapGet(string.Empty, ListarAsync).WithName("ListarNotificacionesImpuestos");
        grupo.MapPost("/{notificacionId:guid}/leida", MarcarLeidaAsync).WithName("MarcarNotificacionLeida");
    }

    private static async Task<IResult> ListarAsync(UsuarioActual usuario, AlmacenCredenciales almacen, CancellationToken ct)
    {
        var empresa = usuario.EmpresaPropia();
        var yo = usuario.Id;
        await using var db = almacen.ContextoDe(empresa);
        var items = await db.Set<Notificacion>().AsNoTracking().OrderByDescending(n => n.CreadaEn).ThenByDescending(n => n.Id).Take(Maximo)
            .Select(n => new NotificacionDto(n.Id, n.Tipo, n.LiquidacionId, n.CreadaEn, n.LeidaPor.Contains(yo)))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> MarcarLeidaAsync(Guid notificacionId, UsuarioActual usuario, AlmacenCredenciales almacen, CancellationToken ct)
    {
        var empresa = usuario.EmpresaPropia();
        var yo = usuario.Id;
        await using var db = almacen.ContextoDe(empresa);
        var fila = await db.Set<Notificacion>().SingleOrDefaultAsync(n => n.Id == notificacionId, ct).ConfigureAwait(false);
        if (fila is null)
            return Results.NotFound();
        if (!fila.LeidaPor.Contains(yo))
        {
            // array_append atómico: dos marcas a la vez no se pisan.
            await db.Database.ExecuteSqlAsync(
                $"UPDATE impuestos.notificaciones SET leida_por = array_append(leida_por, {yo}) WHERE id = {notificacionId} AND tenant_id = {empresa} AND NOT ({yo} = ANY(leida_por))",
                ct).ConfigureAwait(false);
        }

        return Results.NoContent();
    }
}
