using System.Text.Json;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Comparendos;

/// <summary>Quién hizo algo en el timeline.</summary>
public sealed record Actor(ActorTipo Tipo, Guid? UsuarioId, string Nombre)
{
    public static readonly Actor Sistema = new(ActorTipo.Sistema, null, "Sistema");
    public static readonly Actor Scheduler = new(ActorTipo.Scheduler, null, "Scheduler");
    public static readonly Actor Conductor = new(ActorTipo.Conductor, null, "Conductor");

    public static Actor De(UsuarioActual usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return new(ActorTipo.Usuario, usuario.Id, usuario.Nombre);
    }

    public static Actor Api(string nombre) => new(ActorTipo.ApiExterna, null, nombre);
}

/// <summary>
/// Línea de tiempo del comparendo (§3.17): única auditoría transaccional, solo se agregan filas (RN-009). Se escribe
/// en el mismo SaveChanges que el cambio que la origina.
/// </summary>
public sealed class Timeline(ComparendosDb db, Reloj reloj)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TimelineEvento Agregar(Comparendo c, TipoEventoTimeline tipo, string descripcion, Actor actor, object? datos = null, string? relacionadoTipo = null, Guid? relacionadoId = null)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(actor);
        var ahora = reloj.Ahora;
        var evento = new TimelineEvento
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            Tipo = tipo,
            OcurridoEn = ahora,
            ActorTipo = actor.Tipo,
            ActorUsuarioId = actor.UsuarioId,
            ActorNombre = actor.Nombre,
            Descripcion = descripcion,
            Datos = datos is null ? null : JsonSerializer.Serialize(datos, Json),
            RelacionadoTipo = relacionadoTipo,
            RelacionadoId = relacionadoId,
        };
        db.Timeline.Add(evento);
        c.UltimoEventoEn = ahora;
        return evento;
    }
}
