using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Comparendos;

/// <summary>
/// Detalle del comparendo (HU #13497, §7.2): encabezado con el semáforo legal y la ventana de descuento, vehículo y
/// compañía con sus fuentes e historial de cambios, contraventor, cambio de contraventor, avisos, DP y pago; el timeline;
/// las observaciones manuales y la inactivación irreversible.
/// </summary>
public static class DetalleEndpoints
{
    public sealed record NuevaObservacion(string Texto);

    public sealed record Inactivacion(string Motivo);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/comparendos/{id:guid}", Detalle).RequirePermiso(Permisos.BandejaVer);
        api.MapGet("/comparendos/{id:guid}/timeline", Linea).RequirePermiso(Permisos.BandejaVer);
        api.MapGet("/comparendos/{id:guid}/cambios", Cambios).RequirePermiso(Permisos.BandejaVer);
        api.MapPost("/comparendos/{id:guid}/observaciones", Observar).RequirePermiso(Permisos.ComparendoObservar);
        api.MapPost("/comparendos/{id:guid}/inactivar", Inactivar).RequirePermiso(Permisos.ComparendoInactivar);
    }

    private static async Task<IResult> Detalle(Guid id, AccesoComparendo acceso, ComparendosDb db, CalendarioService calendario, Reloj reloj, CancellationToken ct)
    {
        var c = await acceso.ParaLeerAsync(id, ct).ConfigureAwait(false);
        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        var s = motor.Evaluar(Plazos.Fechas(c), reloj.Hoy);
        var ot = c.OrganismoTransitoId is { } otId ? await db.OrganismosTransito.AsNoTracking().FirstOrDefaultAsync(o => o.Id == otId, ct).ConfigureAwait(false) : null;
        var nit = c.IdentificacionMonitoreadaId is { } nid ? await db.IdentificacionesMonitoreadas.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nid, ct).ConfigureAwait(false) : null;
        var placa = c.PlacaMonitoreadaId is { } pid ? await db.PlacasMonitoreadas.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Placa).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        var dp = await db.DerechosPeticion.AsNoTracking().FirstOrDefaultAsync(d => d.ComparendoId == id, ct).ConfigureAwait(false);
        var tipologia = dp is null ? null : await db.Tipologias.AsNoTracking().Where(t => t.Id == dp.TipologiaId).Select(t => new { t.Codigo, t.Nombre }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var pago = await db.Pagos.AsNoTracking().FirstOrDefaultAsync(p => p.ComparendoId == id, ct).ConfigureAwait(false);
        var fuentes = await db.ComparendoFuentes.AsNoTracking().Where(f => f.ComparendoId == id).OrderBy(f => f.PrimeraVezEn)
            .Select(f => new { canal = f.Canal.ToString(), f.MunicipioCodigo, f.Presente, f.PrimeraVezEn, f.UltimaVezVistaEn, f.AusenteDesde }).ToListAsync(ct).ConfigureAwait(false);
        var historial = await db.ContraventorHistorial.AsNoTracking().Where(h => h.ComparendoId == id).OrderByDescending(h => h.RegistradoEn)
            .Select(h => new { h.Id, origen = h.Origen.ToString(), h.Motivo, h.DatosAnteriores, h.DatosNuevos, h.ActorNombre, h.RegistradoEn }).ToListAsync(ct).ConfigureAwait(false);
        var documentos = await (from d in db.DocumentosSoporte.AsNoTracking()
                                join a in db.Archivos.AsNoTracking() on d.ArchivoId equals a.Id
                                where d.ComparendoId == id
                                orderby d.CreadoEn descending
                                select new { d.Id, tipo = d.Tipo.ToString(), d.Vigente, d.CreadoEn, archivoId = a.Id, nombre = a.NombreOriginal, a.ContentType }).ToListAsync(ct).ConfigureAwait(false);
        var solicitudes = await db.SolicitudesCambio.AsNoTracking().Where(x => x.ComparendoId == id).OrderByDescending(x => x.NumeroIntento)
            .Select(x => new { x.Id, x.NumeroIntento, estado = x.Estado.ToString(), x.CausalRechazo, x.FechaRespuestaOt, x.SolicitadoEn, x.RespondidoEn, x.FormatoArchivoId, x.SoporteRespuestaArchivoId })
            .ToListAsync(ct).ConfigureAwait(false);
        var avisos = await db.AvisosConductor.AsNoTracking().Where(a => a.ComparendoId == id).OrderByDescending(a => a.RegistradoEn)
            .Select(a => new { a.Id, tipo = a.TipoAviso.ToString(), disparo = a.Disparo.ToString(), ventana = a.VentanaAlEnvio.ToString(), a.PorcentajeDescuento, a.CorreoDestino, a.RegistradoEn, a.ComunicacionId })
            .ToListAsync(ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            c.Id,
            c.TenantId,
            c.Numero,
            c.Placa,
            estadoProceso = ConsultaBandeja.EstadoProceso(c, dp?.RespuestaSentido),
            ultimoHito = ConsultaBandeja.UltimoHito(c),
            situacion = new
            {
                estadoLegal = s.Estado.ToString(),
                diasHabilesRestantes = s.Estado == Domain.Plazos.EstadoLegal.EnTiempo ? s.HabilesRestantesGestion : null,
                proximoAVencer = c.Activo && !c.Pagado && !c.TieneDp && s.ProximoAVencer,
                ventanaDescuento = Plazos.Ventana(s, c).ToString(),
                finDescuentoVigente = s.FinDescuentoVigente,
                diasHabilesDescuento = s.HabilesRestantesDescuento,
            },
            plazos = Plazos.Fechas(c),
            fuente = ConsultaBandeja.FuenteDe(c.TipoIngreso).ToString(),
            tipoIngreso = c.TipoIngreso.ToString(),
            fuentePrincipal = c.FuentePrincipal?.ToString(),
            vehiculo = new { c.Placa, c.TipoVehiculo, c.ServicioVehiculo },
            compania = new
            {
                c.NitConsultado,
                titular = c.NombreInfractor ?? nit?.NombreTitular,
                tipoDocumentoInfractor = c.TipoDocumentoInfractor?.ToString(),
                c.NumeroDocumentoInfractor,
                nitMonitoreado = nit is null ? null : new { nit.Id, nit.NumeroBase, nit.DigitoVerificacion, nit.NombreTitular },
                placaMonitoreada = placa,
            },
            infraccion = new { codigo = c.CodigoInfraccion, descripcion = c.DescripcionInfraccion, direccion = c.DireccionInfraccion, c.FechaImposicion, c.HoraImposicion, c.FechaNotificacion, esFotomulta = c.EsFotomulta },
            organismo = ot is null ? null : new { ot.Id, ot.Codigo, ot.Nombre, ot.Municipio, ot.Departamento, tieneCorreo = ot.CorreosRadicacion.Count > 0 },
            c.SecretariaTexto,
            valores = new { c.ValorCapital, c.ValorAdicional, c.ValorTotal },
            c.EstadoFuente,
            c.EnCobroCoactivo,
            evidencia = new { c.ArchivoEvidenciaId, c.FotoEvidenciaUrl },
            c.Activo,
            motivoInactivacion = c.MotivoInactivacion?.ToString(),
            c.DetalleInactivacion,
            c.InactivadoEn,
            estadoGestion = c.EstadoGestion.ToString(),
            estadoNotificacionConductor = c.EstadoNotificacionConductor.ToString(),
            estadoCambioContraventor = c.EstadoCambioContraventor.ToString(),
            estadoConsultaContraventor = c.EstadoConsultaContraventor.ToString(),
            c.NotificadoFisicamente,
            c.Pagado,
            c.TieneDp,
            c.Gestionado,
            contraventor = c.Contraventor is { } k ? new
            {
                tipoDocumento = k.TipoDocumento.ToString(), k.NumeroDocumento, k.Nombres, k.Correo, k.Telefono, k.Direccion, k.Ciudad, k.Sucursal, k.TipoContrato,
                origen = k.Origen.ToString(), k.CreadoEn,
            } : null,
            historialContraventor = historial,
            documentos,
            solicitudesCambio = solicitudes,
            avisos,
            dp = dp is null ? null : new
            {
                dp.Id,
                dp.Radicado,
                tipologia,
                estado = dp.Estado.ToString(),
                dp.FechaRadicacion,
                dp.FechaLimiteRespuestaOt,
                dp.CorreosDestino,
                dp.DocumentoArchivoId,
                dp.ComunicacionId,
                respuesta = dp.RespuestaSentido is null ? null : new
                {
                    sentido = dp.RespuestaSentido.ToString(), origen = dp.RespuestaOrigen?.ToString(), dp.RespuestaRadicadoOt, dp.RespuestaFecha, dp.RespuestaArchivoId, dp.RespuestaRegistradaEn,
                },
            },
            pago = pago is null ? null : new { tipo = pago.TipoPago.ToString(), pago.ValorPagado, pago.FechaPago, pago.ComprobanteArchivoId, origen = pago.Origen.ToString(), pago.RegistradoEn, pago.RegistradoPorNombre },
            fuentes,
            c.CreadoEn,
            c.UltimoEventoEn,
        });
    }

    private static async Task<IResult> Linea(Guid id, string? tipo, AccesoComparendo acceso, ComparendosDb db, CancellationToken ct)
    {
        await acceso.ParaLeerAsync(id, ct).ConfigureAwait(false);
        var q = db.Timeline.AsNoTracking().Where(t => t.ComparendoId == id);
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            var tipos = tipo.Split(',').Select(x => Enum.TryParse<TipoEventoTimeline>(x, true, out var e) ? (TipoEventoTimeline?)e : null).OfType<TipoEventoTimeline>().ToList();
            q = q.Where(t => tipos.Contains(t.Tipo));
        }

        var eventos = await q.OrderByDescending(t => t.OcurridoEn).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, tipo = t.Tipo.ToString(), t.OcurridoEn, actorTipo = t.ActorTipo.ToString(), t.ActorNombre, t.Descripcion, t.Datos, t.RelacionadoTipo, t.RelacionadoId })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(eventos);
    }

    private static async Task<IResult> Cambios(Guid id, AccesoComparendo acceso, ComparendosDb db, CancellationToken ct)
    {
        await acceso.ParaLeerAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(await db.ComparendoCambios.AsNoTracking().Where(x => x.ComparendoId == id).OrderByDescending(x => x.RegistradoEn).Take(500)
            .Select(x => new { canal = x.Canal.ToString(), x.Campo, x.ValorAnterior, x.ValorNuevo, x.AplicadoAMaestro, x.RegistradoEn }).ToListAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Observación manual: un hito más del timeline, que no se edita ni se borra (RN-188).</summary>
    private static async Task<IResult> Observar(Guid id, NuevaObservacion pedido, AccesoComparendo acceso, Timeline timeline, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var texto = pedido.Texto?.Trim() ?? string.Empty;
        if (texto.Length is 0 or > 2000)
            throw new ReglaException("OBSERVACION_INVALIDA", "La observación debe tener entre 1 y 2.000 caracteres.");
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var evento = timeline.Agregar(c, TipoEventoTimeline.NotaManual, texto, Actor.De(usuario));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { evento.Id, evento.OcurridoEn });
    }

    /// <summary>
    /// Inactivación manual, irreversible (MOD06 RN-05): pide motivo; la doble confirmación la hace la pantalla. Un
    /// comparendo ya inactivo por desaparición también se puede inactivar para que no se reactive solo (S-28).
    /// </summary>
    private static async Task<IResult> Inactivar(Guid id, Inactivacion pedido, AccesoComparendo acceso, Timeline timeline, UsuarioActual usuario, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var motivo = pedido.Motivo?.Trim() ?? string.Empty;
        if (motivo.Length is < 5 or > 1000)
            throw new ReglaException("MOTIVO_REQUERIDO", "Escribe el motivo de la inactivación (al menos 5 caracteres).");
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        if (c.MotivoInactivacion == MotivoInactivacion.InactivadoOperador)
            throw ReglaException.Conflicto("YA_INACTIVO", "El comparendo ya fue inactivado por un usuario.");

        var ahora = reloj.Ahora;
        c.Activo = false;
        c.MotivoInactivacion = MotivoInactivacion.InactivadoOperador;
        c.DetalleInactivacion = motivo;
        c.InactivadoEn = ahora;
        c.InactivadoPor = usuario.Id;
        await db.PortalEnlaces.Where(p => p.ComparendoId == c.Id && p.RevocadoEn == null)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.RevocadoEn, ahora).SetProperty(p => p.MotivoRevocacion, "COMPARENDO_INACTIVO"), ct).ConfigureAwait(false);
        timeline.Agregar(c, TipoEventoTimeline.InactivadoOperador, $"Inactivado por un usuario: {motivo}", Actor.De(usuario));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
