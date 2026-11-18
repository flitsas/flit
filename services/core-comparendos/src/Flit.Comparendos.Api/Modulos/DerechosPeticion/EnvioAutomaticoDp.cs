using System.Net;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Plantillas;
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
    ServicioPlantillas plantillas,
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
            .Select(e => e.TenantId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var tenant in empresas)
        {
            try
            {
                if (!await productos.ComparendosHabilitadoAsync(tenant, ct).ConfigureAwait(false))
                    continue;
                var r = await EmpresaAsync(tenant, ct).ConfigureAwait(false);
                LogResumen(logger, tenant, r.Enviados, r.Omitidos.Count, r.Rescatados, r.TopeAlcanzado);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una empresa que falla no frena a las demás.
                db.ChangeTracker.Clear();
                LogFallo(logger, tenant, ex);
            }
        }
    }

    /// <summary>Lo que pasó en una empresa en esta corrida.</summary>
    public sealed record ResultadoEmpresa(int Enviados, IReadOnlyList<Omision> Omitidos, int Rescatados, bool TopeAlcanzado);

    /// <summary>Un comparendo que no se envió y por qué (queda en su línea de tiempo).</summary>
    public sealed record Omision(Guid ComparendoId, string Numero, string Codigo, string Motivo);

    /// <summary>Comparendos pendientes cuyo organismo no recibe DP por correo (o no tiene correo de radicación), por organismo.</summary>
    private sealed record OrganismoBloqueado(string Nombre, int Comparendos, string Correccion);

    internal async Task<ResultadoEmpresa> EmpresaAsync(Guid tenant, CancellationToken ct)
    {
        var rescatados = await RescatarSinReaccionAsync(tenant, ct).ConfigureAwait(false);
        var omitidos = new List<Omision>();

        // Sin ninguna tipología habilitada, automatizable y con texto publicado y aprobado por el abogado no hay nada que decidir.
        if (!await HayTipologiaAutomaticaAsync(tenant, ct).ConfigureAwait(false))
        {
            await AvisarOmisionesAsync(tenant, omitidos, [], rescatados, ct).ConfigureAwait(false);
            return new ResultadoEmpresa(0, omitidos, rescatados, false);
        }

        var inicioHoy = new DateTimeOffset(reloj.Hoy.ToDateTime(TimeOnly.MinValue), Domain.Tiempo.ColombiaTime.Offset).ToUniversalTime();
        var admitidos = await (from o in db.OrganismosTransito.AsNoTracking()
                               join d in db.OrganismosTransitoDp.AsNoTracking() on o.Id equals d.OrganismoTransitoId
                               where d.AdmiteDpPorCorreo && o.CorreosRadicacion.Count > 0
                               select o.Id).ToListAsync(ct).ConfigureAwait(false);
        var pendientes = db.Comparendos.AsNoTracking().Where(c => c.TenantId == tenant && c.Activo && !c.Pagado && !c.TieneDp);
        var candidatos = await pendientes.Where(c => c.OrganismoTransitoId != null && admitidos.Contains(c.OrganismoTransitoId.Value))
            .OrderBy(c => c.CreadoEn).ThenBy(c => c.Id).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        var bloqueados = await OrganismosBloqueadosAsync(pendientes, admitidos, ct).ConfigureAwait(false);

        var enviados = 0;
        var topeAlcanzado = false;
        foreach (var id in candidatos)
        {
            Proceso resultado;
            try
            {
                resultado = await ProcesarComparendoAsync(tenant, id, inicioHoy, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un comparendo que falla no frena a los demás ni deja la corrida muda cada día: queda anotado y sigue el siguiente.
                db.ChangeTracker.Clear();
                LogComparendo(logger, id, ex);
                var detalle = ex is ReglaException r ? $" ({r.Message})" : string.Empty;
                var numero = await db.Comparendos.AsNoTracking().Where(x => x.Id == id).Select(x => x.Numero).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
                resultado = new Proceso(EstadoProceso.Omitido, [], new Omision(id, numero, "ERROR_ENVIO", $"ocurrió un error al preparar o enviar el documento{detalle}; el equipo técnico lo revisará."));
                await RegistrarOmisionAsync(resultado.Omision!, ct).ConfigureAwait(false);
            }

            if (resultado.Estado is EstadoProceso.TopeAlcanzado or EstadoProceso.Apagado)
            {
                topeAlcanzado = resultado.Estado == EstadoProceso.TopeAlcanzado;
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

        await AvisarOmisionesAsync(tenant, omitidos, bloqueados, rescatados, ct).ConfigureAwait(false);
        return new ResultadoEmpresa(enviados, omitidos, rescatados, topeAlcanzado);
    }

    private async Task<bool> HayTipologiaAutomaticaAsync(Guid tenant, CancellationToken ct)
    {
        var conjunto = await db.ConfiguracionesEmpresa.AsNoTracking().Where(e => e.TenantId == tenant).Select(e => e.ConjuntoPlantillas).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var automatizables = await db.TipologiasEmpresa.AsNoTracking().Where(h => h.TenantId == tenant && h.Habilitada && h.Automatizable)
            .Select(h => h.TipologiaId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in automatizables)
        {
            if ((await plantillas.PublicadaAsync(conjunto, TipoPlantilla.DpDocumento, id, ct).ConfigureAwait(false))?.RevisadaJuridicamenteEn is not null)
                return true;
        }

        return false;
    }

    private async Task<List<OrganismoBloqueado>> OrganismosBloqueadosAsync(IQueryable<Comparendo> pendientes, List<Guid> admitidos, CancellationToken ct)
    {
        var porOrganismo = await pendientes.Where(c => c.OrganismoTransitoId == null || !admitidos.Contains(c.OrganismoTransitoId.Value))
            .GroupBy(c => c.OrganismoTransitoId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        var ids = porOrganismo.Where(x => x.Id != null).Select(x => x.Id!.Value).ToList();
        var nombres = await db.OrganismosTransito.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre, ct).ConfigureAwait(false);
        var conMarca = await db.OrganismosTransitoDp.AsNoTracking().Where(x => ids.Contains(x.OrganismoTransitoId) && x.AdmiteDpPorCorreo).Select(x => x.OrganismoTransitoId).ToListAsync(ct).ConfigureAwait(false);
        return [.. porOrganismo.OrderByDescending(x => x.N).Select(x =>
        {
            if (x.Id is not { } id)
                return new OrganismoBloqueado("Sin organismo identificado", x.N, "identifica el organismo de tránsito de esos comparendos");
            var nombre = nombres.GetValueOrDefault(id, "Organismo sin nombre");
            return conMarca.Contains(id)
                ? new OrganismoBloqueado(nombre, x.N, $"carga el correo de radicación del organismo «{nombre}»")
                : new OrganismoBloqueado(nombre, x.N, $"marca el organismo «{nombre}» como que admite derechos de petición por correo");
        })];
    }

    private enum EstadoProceso { Enviado, Saltado, Omitido, TopeAlcanzado, Apagado }

    private sealed record Proceso(EstadoProceso Estado, IReadOnlyList<Guid> Comunicaciones, Omision? Omision)
    {
        public static readonly Proceso Saltado = new(EstadoProceso.Saltado, [], null);
        public static readonly Proceso Tope = new(EstadoProceso.TopeAlcanzado, [], null);
        public static readonly Proceso Apagado = new(EstadoProceso.Apagado, [], null);
    }

    /// <summary>
    /// Un comparendo. Primero se evalúa SIN candado ni transacción (la mayoría no es elegible o se omite: es lo barato); solo si
    /// va a enviarse se abre la transacción con el candado, que vuelve a validar todo antes de generar nada.
    /// </summary>
    private async Task<Proceso> ProcesarComparendoAsync(Guid tenant, Guid comparendoId, DateTimeOffset inicioHoy, CancellationToken ct)
    {
        var previo = await db.Comparendos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == comparendoId, ct).ConfigureAwait(false);
        if (previo is null || previo.TieneDp || !previo.Activo || previo.Pagado)
            return Proceso.Saltado;
        var evaluacion = await EvaluarAsync(previo, ct).ConfigureAwait(false);
        if (evaluacion.Omision is { } o)
        {
            await RegistrarOmisionAsync(o, ct).ConfigureAwait(false);
            return new Proceso(EstadoProceso.Omitido, [], o);
        }

        if (evaluacion.TipologiaId is null)
            return Proceso.Saltado;
        return await EnviarConCandadoAsync(tenant, comparendoId, inicioHoy, ct).ConfigureAwait(false);
    }

    private sealed record Evaluacion(Guid? TipologiaId, Omision? Omision, PreparacionDp? Preparado);

    /// <summary>Decide la tipología y revisa si se puede enviar solo. Sin efectos: solo lecturas.</summary>
    private async Task<Evaluacion> EvaluarAsync(Comparendo c, CancellationToken ct)
    {
        var decision = await servicio.DecidirAsync(c, soloAutomatizables: true, ct).ConfigureAwait(false);
        if (!decision.EsElegible || decision.Elegida is not { } codigo)
            return new Evaluacion(null, null, null);
        var tipologia = await db.Tipologias.AsNoTracking().FirstAsync(t => t.Codigo == codigo, ct).ConfigureAwait(false);
        var preparado = await servicio.PrepararAsync(c, tipologia.Id, masivo: false, reintento: false, ct).ConfigureAwait(false);
        // Lo que ya no corresponde (otra persona acaba de enviarlo, lo pagaron o lo inactivaron) no es una omisión que avisar.
        if (preparado.Bloqueos.Any(b => b.Codigo is "YA_TIENE_DP" or "COMPARENDO_PAGADO" or "COMPARENDO_INACTIVO"))
            return new Evaluacion(null, null, null);
        var admiteDp = c.OrganismoTransitoId is { } otId
            && await db.OrganismosTransitoDp.AsNoTracking().AnyAsync(x => x.OrganismoTransitoId == otId && x.AdmiteDpPorCorreo, ct).ConfigureAwait(false);
        return ValidarOmision(preparado, admiteDp) is { } bloqueo
            ? new Evaluacion(tipologia.Id, new Omision(c.Id, c.Numero, bloqueo.Codigo, bloqueo.Mensaje), preparado)
            : new Evaluacion(tipologia.Id, null, preparado);
    }

    /// <summary>Candado, decisión, envío y confirmación en una sola transacción.</summary>
    private async Task<Proceso> EnviarConCandadoAsync(Guid tenant, Guid comparendoId, DateTimeOffset inicioHoy, CancellationToken ct)
    {
        Omision? omision = null;
        var proceso = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Serializa a la empresa: el conteo del tope y el envío no se pisan entre corridas.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString()}, 14606))", ct).ConfigureAwait(false);

            // Apagar el interruptor, el módulo de DP o bajar el tope a mitad de una corrida la corta en el siguiente comparendo.
            var cfg = await db.ConfiguracionesEmpresa.AsNoTracking().Where(e => e.TenantId == tenant)
                .Select(e => new { e.DpAutomaticoActivo, e.HasRightPetitionModule, e.DpAutomaticoTopeDiario }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (cfg is not { DpAutomaticoActivo: true, HasRightPetitionModule: true })
                return Proceso.Apagado;
            if (await db.DerechosPeticion.CountAsync(d => d.TenantId == tenant && d.EnvioAutomatico && d.CreadoEn >= inicioHoy, ct).ConfigureAwait(false) >= cfg.DpAutomaticoTopeDiario)
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
            var evaluacion = await EvaluarAsync(c, ct).ConfigureAwait(false);
            if (evaluacion.Omision is { } o)
            {
                omision = o;
                return new Proceso(EstadoProceso.Omitido, [], o);
            }

            if (evaluacion.TipologiaId is not { } tipologiaId)
                return Proceso.Saltado;

            try
            {
                var dp = await servicio.EnviarAsync(c, tipologiaId, Actor.Scheduler, loteId: null, ct).ConfigureAwait(false);

                // El texto con el que realmente se armó el PDF debe tener la aprobación del abogado: si la versión publicada
                // cambió entre la decisión y el envío, no sale (se descarta la transacción entera).
                var revisada = await db.PlantillaVersiones.AsNoTracking().Where(v => v.Id == dp.PlantillaVersionId).Select(v => v.RevisadaJuridicamenteEn).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (revisada is null)
                {
                    LogTextoSinRevisar(logger, comparendoId, dp.PlantillaVersionId);
                    return Proceso.Saltado;
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new Proceso(EstadoProceso.Enviado, dp.ComunicacionIds, null);
            }
            catch (ReglaException ex) when (ex.Codigo is "YA_TIENE_DP" or "COMPARENDO_PAGADO" or "COMPARENDO_INACTIVO")
            {
                // Entre la evaluación y el envío alguien lo envió, lo pagó o lo inactivó: ya no corresponde, no es un error.
                return Proceso.Saltado;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure })
            {
                // Una persona lo envió a la vez (índice único) o Postgres abortó a uno de los dos en el cruce de candados: regla de
                // negocio, no un fallo. La transacción se descarta entera y el comparendo se revisa en la siguiente corrida.
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
    private async Task AvisarOmisionesAsync(Guid tenant, List<Omision> omitidos, List<OrganismoBloqueado> bloqueados, int rescatados, CancellationToken ct)
    {
        if (omitidos.Count == 0 && rescatados == 0 && bloqueados.Count == 0)
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
        var intro = omitidos.Count > 0
            ? $"<p>El envío automático de derechos de petición de hoy no pudo enviar {omitidos.Count} comparendo(s):</p><ul>{filas}</ul><p>Cada uno quedó anotado en la línea de tiempo del comparendo. Cuando se corrija el motivo, saldrá en la siguiente corrida.</p>"
            : string.Empty;
        var sinCanal = bloqueados.Count > 0
            ? $"<p>Hay {bloqueados.Sum(b => b.Comparendos)} comparendo(s) pendientes que el envío automático no toca porque su organismo no puede recibir derechos de petición por correo:</p><ul>"
                + string.Concat(bloqueados.Take(5).Select(b => $"<li><b>{b.Comparendos}</b> en {WebUtility.HtmlEncode(b.Nombre)}: para que salgan, {WebUtility.HtmlEncode(b.Correccion)}.</li>")) + (bloqueados.Count > 5 ? "<li>…</li>" : string.Empty) + "</ul>"
            : string.Empty;
        var rescate = rescatados > 0 ? $"<p>Además, {rescatados} derecho(s) de petición llevan horas sin confirmación de envío; revísalos en el Centro de Mensajes antes de reintentar.</p>" : string.Empty;
        var cuerpo = $"{intro}{sinCanal}{rescate}<p><a href=\"{correo.AppUrl}/comparendos\">Ver los comparendos</a></p>";
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el envío automático del comparendo {Comparendo}; se anota y se sigue con el siguiente.")]
    private static partial void LogComparendo(ILogger logger, Guid comparendo, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El comparendo {Comparendo} no se envió: la versión de texto {Version} usada para el PDF no tiene revisión jurídica.")]
    private static partial void LogTextoSinRevisar(ILogger logger, Guid comparendo, Guid version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo despachar el correo {Comunicacion} del DP automático; lo reintenta el despachador.")]
    private static partial void LogDespacho(ILogger logger, Guid comunicacion, Exception ex);
}
