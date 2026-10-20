using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Avisos;

/// <summary>Envío manual de avisos (individual y masivo) y horario del scheduler (HU #13485, §7.23).</summary>
public static class AvisosEndpoints
{
    public sealed record PedidoAviso(string Tipo);

    public sealed record PedidoMasivo(IReadOnlyList<Guid> ComparendoIds, string Tipo);

    public sealed record HorarioAviso(string HoraEnvio, bool Activo);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/comparendos/{id:guid}/avisos", Enviar).RequirePermiso(Permisos.AvisosEnviar);
        api.MapPost("/avisos/masivo", EnviarMasivo).RequirePermiso(Permisos.AvisosEnviar);
        var admin = api.MapGroup("/admin/scheduler-avisos").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        admin.MapGet("", Horarios);
        admin.MapPut("/{tipo}", GuardarHorario);
    }

    private static TipoAviso Tipo(string texto) =>
        Enum.TryParse<TipoAviso>(texto, true, out var t) && t != TipoAviso.Aviso4 ? t : throw new ReglaException("TIPO_AVISO_INVALIDO", "Elige el aviso 1, 2 o 3 (el 4 sale con el cambio de contraventor).");

    private static async Task<IResult> Enviar(Guid id, PedidoAviso pedido, AccesoComparendo acceso, ServicioAvisos avisos, UsuarioActual usuario, ComparendosDb db, DespachadorCorreos despachador, CancellationToken ct)
    {
        var tipo = Tipo(pedido.Tipo);
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var aviso = await avisos.EnviarAsync(c, tipo, Disparo.Manual, Actor.De(usuario), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var estado = await despachador.DespacharAsync(aviso.ComunicacionId, ct).ConfigureAwait(false);
        return Results.Ok(new { aviso.Id, comunicacion = aviso.ComunicacionId, estado = estado.ToString() });
    }

    private static async Task<IResult> EnviarMasivo(PedidoMasivo pedido, UsuarioActual usuario, ComparendosDb db, ServicioAvisos avisos, CancellationToken ct)
    {
        var tipo = Tipo(pedido.Tipo);
        var tenant = usuario.EmpresaOperacion();
        if (pedido.ComparendoIds is not { Count: > 0 and <= 500 })
            throw new ReglaException("SELECCION_INVALIDA", "Selecciona entre 1 y 500 comparendos.");
        var resultados = new List<object>();
        foreach (var id in pedido.ComparendoIds.Distinct())
        {
            var c = await db.Comparendos.Include(x => x.Contraventor).FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant, ct).ConfigureAwait(false);
            if (c is null)
            {
                resultados.Add(new { comparendoId = id, enviado = false, motivo = "No existe o no es de tu empresa." });
                continue;
            }

            try
            {
                await avisos.EnviarAsync(c, tipo, Disparo.Manual, Actor.De(usuario), ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                resultados.Add(new { comparendoId = id, numero = c.Numero, enviado = true, motivo = (string?)null });
            }
            catch (ReglaException ex)
            {
                db.ChangeTracker.Clear();
                resultados.Add(new { comparendoId = id, numero = c.Numero, enviado = false, motivo = ex.Message });
            }
        }

        return Results.Ok(resultados);
    }

    private static async Task<IResult> Horarios(ComparendosDb db, CancellationToken ct) =>
        Results.Ok(await db.SchedulerAvisos.AsNoTracking().OrderBy(s => s.TipoAviso)
            .Select(s => new { tipo = s.TipoAviso.ToString(), horaEnvio = s.HoraEnvio.ToString("HH:mm"), s.Activo }).ToListAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> GuardarHorario(string tipo, HorarioAviso pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var t = Tipo(tipo);
        if (!TimeOnly.TryParse(pedido.HoraEnvio, System.Globalization.CultureInfo.InvariantCulture, out var hora))
            throw new ReglaException("HORA_INVALIDA", "Escribe la hora como HH:mm.");
        var s = await db.SchedulerAvisos.FirstAsync(x => x.TipoAviso == t, ct).ConfigureAwait(false);
        var cambios = Auditoria.Diferencias(("hora_envio", s.HoraEnvio.ToString("HH:mm"), hora.ToString("HH:mm")), ("activo", s.Activo, pedido.Activo));
        if (cambios.Length > 0)
        {
            (s.HoraEnvio, s.Activo, s.ActualizadoEn, s.ActualizadoPor) = (hora, pedido.Activo, reloj.Ahora, usuario.Id);
            auditoria.Registrar(null, "scheduler_aviso", t, "ACTUALIZAR", cambios);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(new { tipo = t.ToString(), horaEnvio = hora.ToString("HH:mm"), pedido.Activo });
    }
}

/// <summary>
/// Scheduler de avisos (trabajo AVISOS_CONDUCTOR, cada 15 minutos): para cada aviso activo cuya hora cae en el tramo,
/// envía a los comparendos elegibles el aviso que les toca. Máximo un aviso automático por comparendo y día; si
/// coinciden, gana el de número mayor (S-53).
/// </summary>
public sealed partial class SchedulerAvisos(ComparendosDb db, ServicioAvisos avisos, CalendarioService calendario, Reloj reloj, IProductosHabilitados productos, ILogger<SchedulerAvisos> logger) : IEjecutorTrabajo
{
    public bool Atiende(TipoTrabajo tipo) => tipo == TipoTrabajo.AvisosConductor;

    public async Task EjecutarAsync(SolicitudTrabajo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var ahora = TimeOnly.FromDateTime(reloj.AhoraColombia.DateTime);
        var activos = (await db.SchedulerAvisos.AsNoTracking().Where(s => s.Activo).ToListAsync(ct).ConfigureAwait(false))
            .Where(s => solicitud.Disparo == Disparo.Manual || EnTramo(s.HoraEnvio, ahora)).Select(s => s.TipoAviso).ToHashSet();
        if (activos.Count == 0)
            return;

        var hoy = reloj.Hoy;
        var inicioHoy = new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime();
        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        var candidatos = await db.Comparendos.AsNoTracking()
            .Where(c => c.Activo && !c.Pagado && !c.NotificadoFisicamente && c.Contraventor != null && c.Contraventor.Correo != null && c.FechaFinDescuento50 != null)
            .Where(c => solicitud.TenantId == null || c.TenantId == solicitud.TenantId)
            .Where(c => !db.AvisosConductor.Any(a => a.ComparendoId == c.Id && a.Disparo == Disparo.Programado && a.RegistradoEn >= inicioHoy))
            .Select(c => new { c.Id, c.TenantId }).ToListAsync(ct).ConfigureAwait(false);

        // HU #13521: a una empresa sin el producto habilitado no le sale ningún aviso automático.
        var habilitadas = new HashSet<Guid>();
        foreach (var t in candidatos.Select(c => c.TenantId).Distinct())
        {
            if (await productos.ComparendosHabilitadoAsync(t, ct).ConfigureAwait(false))
                habilitadas.Add(t);
        }

        var ids = candidatos.Where(c => habilitadas.Contains(c.TenantId)).Select(c => c.Id).ToList();
        foreach (var id in ids)
        {
            try
            {
                var c = await db.Comparendos.Include(x => x.Contraventor).FirstAsync(x => x.Id == id, ct).ConfigureAwait(false);
                var ventana = Comparendos.Plazos.Ventana(motor.Evaluar(Comparendos.Plazos.Fechas(c), hoy), c);
                if (await avisos.AutomaticoQueTocaAsync(c, ventana, ct).ConfigureAwait(false) is not { } tipo || !activos.Contains(tipo))
                    continue;
                // S-33: tras reemplazar el conductor, el nuevo recibe sus propios avisos automáticos (la unicidad es por destinatario).
                var destino = c.Contraventor?.Correo;
                if (await db.AvisosConductor.AnyAsync(a => a.ComparendoId == id && a.TipoAviso == tipo && a.Disparo == Disparo.Programado && a.CorreoDestino == destino, ct).ConfigureAwait(false))
                    continue;
                await avisos.EnviarAsync(c, tipo, Disparo.Programado, Actor.Scheduler, ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (ReglaException ex)
            {
                LogOmitido(logger, id, ex.Codigo);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>La hora del aviso cayó en los últimos 15 minutos.</summary>
    public static bool EnTramo(TimeOnly hora, TimeOnly ahora)
    {
        var diferencia = (ahora - hora).TotalMinutes;
        return diferencia is >= 0 and < 15;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso omitido para {Comparendo}: {Codigo}.")]
    private static partial void LogOmitido(ILogger logger, Guid comparendo, string codigo);
}
