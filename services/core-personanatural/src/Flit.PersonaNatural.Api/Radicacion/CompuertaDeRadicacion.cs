using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>
/// Implementación de <see cref="ICompuertaDeRadicacion"/> (HU #14046). Mira SOLO las personas de la solicitud: nunca busca por
/// cédula, así que una validación aprobada de otra solicitud de la misma persona no cuenta. Una parte cuenta como verificada
/// solo con <c>validacion_id</c> y estado <c>aprobada</c> de ESTA solicitud, sin importar fechas.
/// <para>
/// Antes de decidir, si <see cref="IdentidadOptions.ReconciliarAlConsultar"/>, pregunta a FLIT 2 por las validaciones que el
/// espejo aún ve abiertas: el evento puede no haber llegado (bus apagado, aviso perdido) y bloquear a quien ya validó sería
/// un falso 409. Si FLIT 2 no responde, queda lo que el espejo sabe, que es lo seguro: pendiente.
/// </para>
/// </summary>
public sealed class CompuertaDeRadicacion(PersonaNaturalDb db, IReglasDeIdentidad reglas, IdentidadService identidad, IOptions<IdentidadOptions> opciones) : ICompuertaDeRadicacion
{
    public async Task<ResultadoCompuerta> EvaluarAsync(Guid solicitudId, CancellationToken ct)
    {
        var definidas = await reglas.LeerAsync(solicitudId, ct).ConfigureAwait(false);
        // Una solicitud inexistente no tiene nada verificado: no se puede radicar.
        if (definidas is null)
            return ResultadoCompuerta.Bloqueada(MotivosCompuerta.IdentidadPendiente, []);
        if (definidas.RolesConIdentidad.Count == 0)
            return ResultadoCompuerta.Aprobada;

        if (opciones.Value.ReconciliarAlConsultar)
            await identidad.ReconciliarAsync(solicitudId, ct).ConfigureAwait(false);

        var personas = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == solicitudId)
            .Select(p => new { p.Parte, p.Ordinal, p.ValidacionId, p.ValidacionEstado }).ToListAsync(ct).ConfigureAwait(false);

        var faltantes = new List<ParteFaltante>();
        foreach (var regla in definidas.RolesConIdentidad)
        {
            var delRol = personas.Where(p => string.Equals(p.Parte, regla.Rol, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Ordinal).ToList();
            foreach (var p in delRol)
            {
                var verificada = p.ValidacionId is not null && p.ValidacionEstado == EstadosIdentidad.Aprobada;
                if (!verificada)
                    faltantes.Add(new ParteFaltante(regla.Rol, p.Ordinal, p.ValidacionEstado));
            }

            // El rol exige al menos `min` personas: las que aún no se han registrado también faltan.
            for (var ordinal = delRol.Count + 1; ordinal <= regla.Min; ordinal++)
                faltantes.Add(new ParteFaltante(regla.Rol, (short)ordinal, EstadosIdentidad.SinSolicitar));
        }

        if (faltantes.Count == 0)
            return ResultadoCompuerta.Aprobada;

        var motivo = faltantes.Any(f => f.Estado == EstadosIdentidad.Rechazada) ? MotivosCompuerta.IdentidadRechazada : MotivosCompuerta.IdentidadPendiente;
        return ResultadoCompuerta.Bloqueada(motivo, faltantes);
    }
}

public static class CompuertaDeRadicacionExtensions
{
    /// <summary>
    /// Registra la compuerta definitiva. <c>TryAdd</c>: si otra HU ya dejó una implementación por defecto, esta no la pisa
    /// por orden de registro; el integrador deja una sola.
    /// </summary>
    public static IServiceCollection AddCompuertaDeRadicacion(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ICompuertaDeRadicacion, CompuertaDeRadicacion>();
        return services;
    }
}
