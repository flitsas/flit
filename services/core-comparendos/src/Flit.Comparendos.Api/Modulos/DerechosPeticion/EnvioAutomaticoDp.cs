using System.Net;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Comparendos.Api.Modulos.DerechosPeticion;

/// <summary>
/// Envío automático de derechos de petición (Epic #14606, HU #14609). Una corrida por día hábil (y «Ejecutar ahora» del
/// SuperAdmin, que SÍ envía correos): para cada empresa que lo tiene encendido, decide la tipología de cada comparendo con
/// <see cref="ServicioDp.DecidirAsync"/> (solo tipologías automatizables con revisión jurídica) y lo envía con
/// <see cref="ServicioDp.EnviarAsync"/>. Apagado por defecto: sin <c>DpAutomaticoActivo</c> no toca nada.
/// </summary>
/// <remarks>
/// <para>No duplica: cada comparendo se reclama con <c>FOR UPDATE SKIP LOCKED</c> dentro de una transacción ANTES de
/// reservar radicado, generar el PDF o registrar el correo. Si otra corrida (u otra réplica) lo tiene, se salta; si ya
/// quedó con DP al obtener el candado, se salta; y si una persona lo envía a la vez, la violación del índice único
/// <c>ix_derecho_peticion_comparendo_id</c> se trata como regla de negocio («ya tiene DP»): la transacción se revierte
/// completa, sin radicados reservados ni filas de archivo huérfanas.</para>
/// <para>El tope diario se cuenta sobre los DP automáticos de hoy dentro de la misma transacción, bajo un candado
/// consultivo por empresa, así que dos corridas simultáneas no lo sobrepasan. Lo que no cabe queda para el siguiente día
/// hábil (los candidatos se recorren siempre en el mismo orden: el más antiguo primero).</para>
/// </remarks>
public sealed partial class EnvioAutomaticoDp(
    ComparendosDb db,
    ServicioDp servicio,
    Capacidades capacidades,
    ServicioCorreo correo,
    IUsuariosEmpresa usuarios,
    IProductosHabilitados productos,
    Timeline timeline,
    DespachadorCorreos despachador,
    Reloj reloj,
    ILogger<EnvioAutomaticoDp> logger) : IEjecutorTrabajo
{
    /// <summary>Un DP en «Enviando» más de este tiempo sin que el correo reaccione se marca para revisión.</summary>
    public static readonly TimeSpan EsperaReaccion = TimeSpan.FromHours(2);

    public bool Atiende(TipoTrabajo tipo) => tipo == TipoTrabajo.EnvioAutomaticoDp;

    public async Task EjecutarAsync(SolicitudTrabajo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var empresas = await db.ConfiguracionesEmpresa.AsNoTracking()
            .Where(e => e.DpAutomaticoActivo && e.HasRightPetitionModule && (solicitud.TenantId == null || e.TenantId == solicitud.TenantId))
            .Select(e => new { e.TenantId, e.DpAutomaticoTopeDiario }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var e in empresas)
        {
            try
            {
                if (!await productos.ComparendosHabilitadoAsync(e.TenantId, ct).ConfigureAwait(false))
                    continue;
                var r = await EmpresaAsync(e.TenantId, e.DpAutomaticoTopeDiario, ct).ConfigureAwait(false);
                LogResumen(logger, e.TenantId, r.Enviados, r.Omitidos.Count, r.Rescatados, r.TopeAlcanzado);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una empresa que falla no frena a las demás.
                db.ChangeTracker.Clear();
                LogFallo(logger, e.TenantId, ex);
            }
        }
    }

    /// <summary>Lo que pasó en una empresa en esta corrida.</summary>
    public sealed record ResultadoEmpresa(int Enviados, IReadOnlyList<Omision> Omitidos, int Rescatados, bool TopeAlcanzado);

    /// <summary>Un comparendo que no se envió y por qué (queda en su línea de tiempo).</summary>
    public sealed record Omision(Guid ComparendoId, string Numero, string Codigo, string Motivo);

    internal async Task<ResultadoEmpresa> EmpresaAsync(Guid tenant, int tope, CancellationToken ct)
    {
        var rescatados = await RescatarSinReaccionAsync(tenant, ct).ConfigureAwait(false);
        var inicioHoy = new DateTimeOffset(reloj.Hoy.ToDateTime(TimeOnly.MinValue), Domain.Tiempo.ColombiaTime.Offset).ToUniversalTime();
        var candidatos = await db.Comparendos.AsNoTracking()
            .Where(c => c.TenantId == tenant && c.Activo && !c.Pagado && !c.TieneDp && !db.DerechosPeticion.Any(d => d.ComparendoId == c.Id))
            .OrderBy(c => c.CreadoEn).ThenBy(c => c.Id).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);

        var enviados = 0;
        var omitidos = new List<Omision>();
        var topeAlcanzado = false;
        foreach (var id in candidatos)
        {
            var resultado = await ProcesarAsync(tenant, id, tope, inicioHoy, ct).ConfigureAwait(false);
            if (resultado.Estado == EstadoProceso.TopeAlcanzado)
            {
                topeAlcanzado = true;
                break;
            }

            if (resultado.Estado == EstadoProceso.Enviado)
            {
                enviados++;
                await DespacharAsync(resultado.Comunicaciones, ct).ConfigureAwait(false);
            }
            else if (resultado.Omision is not null)
            {
                omitidos.Add(resultado.Omision);
            }
        }

        await AvisarOmisionesAsync(tenant, omitidos, rescatados, ct).ConfigureAwait(false);
        return new ResultadoEmpresa(enviados, omitidos, rescatados, topeAlcanzado);
    }

    private enum EstadoProceso { Enviado, Saltado, Omitido, TopeAlcanzado }

    private sealed record Proceso(EstadoProceso Estado, IReadOnlyList<Guid> Comunicaciones, Omision? Omision)
    {
        public static readonly Proceso Saltado = new(EstadoProceso.Saltado, [], null);
        public static readonly Proceso Tope = new(EstadoProceso.TopeAlcanzado, [], null);
    }

    /// <summary>Un comparendo: candado, decisión, envío y confirmación en una sola transacción.</summary>
    private async Task<Proceso> ProcesarAsync(Guid tenant, Guid comparendoId, int tope, DateTimeOffset inicioHoy, CancellationToken ct)
    {
        Omision? omision = null;
        var proceso = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Serializa a la empresa: el conteo del tope y el envío no se pisan entre corridas.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString()}, 14606))", ct).ConfigureAwait(false);
            if (await db.DerechosPeticion.CountAsync(d => d.TenantId == tenant && d.EnvioAutomatico && d.CreadoEn >= inicioHoy, ct).ConfigureAwait(false) >= tope)
                return Proceso.Tope;

            // Reclamo atómico ANTES de generar nada: quien ya lo tiene bloqueado (otra corrida) lo hace; yo paso al siguiente.
            var reclamado = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM comparendos.comparendo WHERE id = {comparendoId} AND NOT tiene_dp FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct).ConfigureAwait(false);
            if (reclamado.Count == 0)
                return Proceso.Saltado;

            var c = await db.Comparendos.Include(x => x.Contraventor).FirstAsync(x => x.Id == comparendoId, ct).ConfigureAwait(false);
            if (c.TieneDp || !c.Activo || c.Pagado || await db.DerechosPeticion.AnyAsync(d => d.ComparendoId == c.Id, ct).ConfigureAwait(false))
                return Proceso.Saltado;

            // Re-valida con el candado puesto: tipología aplicable, automatizable y con la revisión jurídica de hoy.
            var decision = await servicio.DecidirAsync(c, soloAutomatizables: true, ct).ConfigureAwait(false);
            if (!decision.EsElegible || decision.Elegida is not { } codigo)
                return Proceso.Saltado;
            var tipologia = await db.Tipologias.AsNoTracking().FirstAsync(t => t.Codigo == codigo, ct).ConfigureAwait(false);

            var preparado = await servicio.PrepararAsync(c, tipologia.Id, masivo: false, reintento: false, ct).ConfigureAwait(false);
            var admiteDp = c.OrganismoTransitoId is { } otId
                && await db.OrganismosTransitoDp.AnyAsync(x => x.OrganismoTransitoId == otId && x.AdmiteDpPorCorreo, ct).ConfigureAwait(false);
            if (ValidarOmision(preparado, admiteDp) is { } bloqueo)
            {
                omision = new Omision(c.Id, c.Numero, bloqueo.Codigo, bloqueo.Mensaje);
                return new Proceso(EstadoProceso.Omitido, [], omision);
            }

            try
            {
                var dp = await servicio.EnviarAsync(c, tipologia.Id, Actor.Scheduler, loteId: null, ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new Proceso(EstadoProceso.Enviado, dp.ComunicacionIds, null);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Una persona lo envió a la vez: regla de negocio, no un fallo. La transacción se descarta entera.
                return Proceso.Saltado;
            }
        }).ConfigureAwait(false);

        db.ChangeTracker.Clear();
        if (proceso.Estado == EstadoProceso.Omitido && omision is not null)
            await RegistrarOmisionAsync(omision, ct).ConfigureAwait(false);
        return proceso;
    }

    /// <summary>
    /// Por qué un comparendo decidido NO se puede enviar solo, o <c>null</c> si va. Con cualquier bloqueo de la preparación
    /// (sin correo de radicación, propietario ajeno, sin plantilla...) se omite en vez de dejar que el envío falle.
    /// </summary>
    private static Bloqueo? ValidarOmision(PreparacionDp p, bool organismoAdmiteDp)
    {
        // HU #14610: el automático solo habla con organismos que aceptan DP por correo y solo con un representante que firma.
        if (!organismoAdmiteDp)
            return new Bloqueo("ORGANISMO_NO_ADMITE_DP_CORREO", "el organismo de tránsito no está marcado como que recibe derechos de petición por correo.");
        if (p.Representante is null)
            return new Bloqueo("SIN_REPRESENTANTE_LEGAL", "no hay un representante legal activo con permiso para firmar derechos de petición.");
        if (p.Representante.FirmaArchivoId is null)
            return new Bloqueo("SIN_FIRMA_REPRESENTANTE", "el representante legal no tiene su firma cargada.");
        return p.Bloqueos.Count > 0 ? p.Bloqueos[0] : null;
    }

    /// <summary>Deja el motivo en la línea de tiempo del comparendo, una vez por motivo (no repite cada día lo mismo).</summary>
    private async Task RegistrarOmisionAsync(Omision o, CancellationToken ct)
    {
        var c = await db.Comparendos.FirstAsync(x => x.Id == o.ComparendoId, ct).ConfigureAwait(false);
        var descripcion = $"No se envió el derecho de petición automático: {o.Motivo}";
        var ultima = await db.Timeline.AsNoTracking().Where(t => t.ComparendoId == c.Id && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido)
            .OrderByDescending(t => t.OcurridoEn).Select(t => t.Descripcion).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (ultima == descripcion)
            return;
        timeline.Agregar(c, TipoEventoTimeline.DpAutomaticoOmitido, descripcion, Actor.Scheduler, new { motivo = o.Codigo });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// DP automáticos que siguen en «Enviando» pasado el plazo sin que el correo reaccione: no se reenvían (podrían haber
    /// salido); se marcan para revisión en la línea de tiempo, una sola vez cada uno.
    /// </summary>
    private async Task<int> RescatarSinReaccionAsync(Guid tenant, CancellationToken ct)
    {
        var limite = reloj.Ahora - EsperaReaccion;
        var colgados = await db.DerechosPeticion.AsNoTracking()
            .Where(d => d.TenantId == tenant && d.EnvioAutomatico && d.Estado == EstadoDp.Enviando && d.CreadoEn < limite)
            .Where(d => !db.Timeline.Any(t => t.RelacionadoId == d.Id && t.Tipo == TipoEventoTimeline.DpEnviandoSinReaccion))
            .Select(d => new { d.Id, d.ComparendoId, d.Radicado }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var d in colgados)
        {
            var c = await db.Comparendos.FirstAsync(x => x.Id == d.ComparendoId, ct).ConfigureAwait(false);
            timeline.Agregar(c, TipoEventoTimeline.DpEnviandoSinReaccion,
                $"El derecho de petición {d.Radicado} lleva más de {(int)EsperaReaccion.TotalHours} horas en «Enviando» sin confirmación del correo. No se reenvía solo: revisa en el Centro de Mensajes si salió antes de reintentar.",
                Actor.Scheduler, new { d.Radicado }, "derecho_peticion", d.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
        }

        return colgados.Count;
    }

    /// <summary>
    /// Resumen de lo omitido (y de los DP sin reacción) para los encargados de la empresa: a sus correos de alerta operativa
    /// o, si no tiene, a quienes gestionan representantes. Máximo uno por empresa y día, aunque el trabajo corra varias veces.
    /// </summary>
    private async Task AvisarOmisionesAsync(Guid tenant, List<Omision> omitidos, int rescatados, CancellationToken ct)
    {
        if (omitidos.Count == 0 && rescatados == 0)
            return;
        var inicioHoy = new DateTimeOffset(reloj.Hoy.ToDateTime(TimeOnly.MinValue), Domain.Tiempo.ColombiaTime.Offset).ToUniversalTime();
        if (await db.Comunicaciones.AnyAsync(x => x.TenantId == tenant && x.Tipo == TipoComunicacion.ResumenEnvioAutomaticoDp && x.CreadoEn >= inicioHoy, ct).ConfigureAwait(false))
            return;

        var config = await capacidades.DeAsync(tenant, ct).ConfigureAwait(false);
        var para = config.CorreosAlertaOperativa.Count > 0
            ? config.CorreosAlertaOperativa.Select(x => new Destinatario(x)).ToList()
            : (await usuarios.ConPermisoAsync(tenant, Permisos.RepresentantesGestionar, ct).ConfigureAwait(false)).Select(u => new Destinatario(u.Correo, u.Nombre)).ToList();
        if (para.Count == 0)
            return;

        var porMotivo = omitidos.GroupBy(o => o.Motivo).OrderByDescending(g => g.Count()).ToList();
        var filas = string.Concat(porMotivo.Select(g =>
            $"<li><b>{g.Count()}</b> comparendo(s): {WebUtility.HtmlEncode(g.Key)} <span style=\"color:#5B6B82\">({WebUtility.HtmlEncode(string.Join(", ", g.Take(5).Select(o => o.Numero)))}{(g.Count() > 5 ? ", …" : string.Empty)})</span></li>"));
        var rescate = rescatados > 0 ? $"<p>Además, {rescatados} derecho(s) de petición llevan horas sin confirmación de envío; revísalos en el Centro de Mensajes antes de reintentar.</p>" : string.Empty;
        var cuerpo = $"<p>El envío automático de derechos de petición de hoy no pudo enviar {omitidos.Count} comparendo(s):</p><ul>{filas}</ul>{rescate}"
            + $"<p>Cada uno quedó anotado en la línea de tiempo del comparendo. Cuando se corrija el motivo, saldrá en la siguiente corrida.</p><p><a href=\"{correo.AppUrl}/comparendos\">Ver los comparendos</a></p>";
        var marcaFlit = config.ConjuntoPlantillas == ConjuntoPlantillas.Flit;
        var enviados = await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = tenant,
            Tipo = TipoComunicacion.ResumenEnvioAutomaticoDp,
            Plantilla = "comparendos.resumen_envio_automatico_dp",
            Para = para,
            Asunto = "Derechos de petición automáticos: hay comparendos sin enviar",
            Html = HtmlCorreo.Envolver("Derechos de petición automáticos", cuerpo, marcaFlit: marcaFlit),
            Canal = CanalCorreo.FlitSmtp,
            RemitenteNombre = marcaFlit ? "FLIT Comparendos" : config.RemitenteNombre ?? config.EmpresaNombre,
        }, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await DespacharAsync([.. enviados.Select(e => e.Id)], ct).ConfigureAwait(false);
    }

    private async Task DespacharAsync(IReadOnlyList<Guid> comunicaciones, CancellationToken ct)
    {
        foreach (var id in comunicaciones)
        {
            try
            {
                await despachador.DespacharAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // El ciclo del despachador lo reintenta; el DP ya quedó registrado.
                LogDespacho(logger, id, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Envío automático de DP, empresa {Empresa}: {Enviados} enviados, {Omitidos} omitidos, {Rescatados} sin reacción; tope alcanzado: {Tope}.")]
    private static partial void LogResumen(ILogger logger, Guid empresa, int enviados, int omitidos, int rescatados, bool tope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el envío automático de DP de la empresa {Empresa}.")]
    private static partial void LogFallo(ILogger logger, Guid empresa, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo despachar el correo {Comunicacion} del DP automático; lo reintenta el despachador.")]
    private static partial void LogDespacho(ILogger logger, Guid comunicacion, Exception ex);
}
