using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>
/// GDC-EVID §10: el trabajador de «Asociar los N». Reclama de a UNA fila de <c>asociacion_lote_documento</c> (no el documento)
/// con <c>FOR UPDATE SKIP LOCKED</c> y una concesión de 10 minutos, como <see cref="ProcesadorOcr"/>. Actúa como quien pidió el
/// lote, asocia con la misma <see cref="ServicioOcr.AsociarAsync"/> y deja el correo de evidencia EN COLA. Reclamar la fila hija
/// permite retomar: si el proceso cae tras confirmar la asociación, la fila sigue pendiente y la vuelta siguiente solo registra
/// el correo. Si algo cambió (otra persona le puso documento, ya no es asociable…), el documento vuelve a Duplicado con su motivo.
/// </summary>
public sealed partial class ProcesadorAsociacionLote(IServiceScopeFactory scopes, TimeProvider time, ILogger<ProcesadorAsociacionLote> logger) : BackgroundService
{
    /// <summary>Errores técnicos seguidos antes de devolver el documento a Duplicado (se puede volver a pedir).</summary>
    internal const int IntentosMaximos = 3;

    public const string MotivoErrorTecnico = "No se pudo asociar este documento ahora. Vuelve a intentarlo.";
    public const string MotivoCambio = "El documento cambió mientras esperaba: ábrelo para revisarlo.";
    public const string MotivoOtroDocumento = "Otra persona le puso un documento a este comparendo mientras tanto; revísalo uno por uno.";
    public const string MotivoYaNoAsociable = "Ya no se puede asociar: revísalo uno por uno.";

    /// <summary>
    /// Los motivos de <see cref="ServicioOcr.AsociarAsync"/> están escritos para la pantalla individual («mientras lo revisabas»); en
    /// el lote nadie estaba revisando, así que se traducen a lo que pasó. Solo en el lote; la pantalla individual no cambia.
    /// </summary>
    internal static string MotivoDelLote(ReglaException ex) => ex.Codigo switch
    {
        "EVIDENCIA_CAMBIO" or "EVIDENCIA_EXISTENTE" => MotivoOtroDocumento,
        "NO_ASOCIABLE" => MotivoYaNoAsociable,
        _ => ex.Message,
    };

    private sealed record Reclamo(Guid LoteId, Guid DocumentoId, short Intentos);

    /// <summary>Solo pruebas: se llama al empezar cada documento (para simular un error técnico del trabajador).</summary>
    internal Action<Guid>? AntesDeProcesar { get; set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ProcesarSiguienteAsync(stoppingToken).ConfigureAwait(false))
                {
                }
            }
            // No solo «no es una cancelación»: el plazo propio de un documento también lanza OperationCanceledException, y si
            // saliera de aquí el host detendría el servicio entero.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogFallo(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), time, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Procesa la siguiente fila pendiente; false si no hay ninguna disponible.</summary>
    public async Task<bool> ProcesarSiguienteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var ahora = time.GetUtcNow();
        var reclamos = await db.Database.SqlQuery<Reclamo>($"""
            UPDATE comparendos.asociacion_lote_documento SET proximo_intento_en = {ahora.AddMinutes(10)}, intentos = intentos + 1
            WHERE (lote_id, documento_id) = (
                SELECT lote_id, documento_id FROM comparendos.asociacion_lote_documento
                WHERE resultado = 'PorAsociar' AND (proximo_intento_en IS NULL OR proximo_intento_en <= {ahora})
                ORDER BY lote_id, documento_id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING lote_id, documento_id, intentos
            """).ToListAsync(ct).ConfigureAwait(false);
        if (reclamos.Count == 0)
        {
            // Si un cierre falló justo al final, el lote queda sin fecha de término: se repara cuando la cola está vacía.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE comparendos.asociacion_lote l SET terminado_en = {ahora}
                WHERE l.terminado_en IS NULL
                  AND NOT EXISTS (SELECT 1 FROM comparendos.asociacion_lote_documento i WHERE i.lote_id = l.id AND i.resultado = 'PorAsociar')
                """, ct).ConfigureAwait(false);
            return false;
        }

        var r = reclamos[0];
        var servicio = scope.ServiceProvider.GetRequiredService<ServicioOcr>();
        var lote = await db.AsociacionesLote.AsNoTracking().FirstAsync(l => l.Id == r.LoteId, ct).ConfigureAwait(false);
        var actor = new Actor(ActorTipo.Usuario, lote.SolicitadoPor, lote.SolicitadoPorNombre);
        try
        {
            AntesDeProcesar?.Invoke(r.DocumentoId);
            await ProcesarAsync(scope.ServiceProvider, db, servicio, lote.TenantId, lote.PaqueteId, lote.SolicitadoEn, r, actor, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Un error técnico: la concesión vence y se reintenta; al tercero el documento vuelve a Duplicado con un motivo.
            LogFallo(logger, ex);
            db.ChangeTracker.Clear();
            if (r.Intentos < IntentosMaximos)
            {
                // Reintento pronto, no a los 10 minutos de la concesión.
                var pronto = time.GetUtcNow().AddSeconds(30);
                await db.AsociacionLoteDocumentos.Where(i => i.LoteId == r.LoteId && i.DocumentoId == r.DocumentoId && i.Resultado == ResultadoAsociacionLote.PorAsociar)
                    .ExecuteUpdateAsync(u => u.SetProperty(i => i.ProximoIntentoEn, pronto), ct).ConfigureAwait(false);
            }
            else if (await db.DocumentosOcr.AsNoTracking().AnyAsync(d => d.Id == r.DocumentoId && d.Estado == EstadoDocumentoOcr.Asociado, ct).ConfigureAwait(false))
            {
                // Sí quedó asociado (lo que falló fue el correo): no se dice «no se asoció».
                await CerrarAsync(db, r, ResultadoAsociacionLote.Asociado, null,
                    new ResultadoEvidencia(ResultadoEvidencia.Omitido, "No se pudo preparar el correo al conductor; el documento quedó asociado"), ct).ConfigureAwait(false);
            }
            else
            {
                await NoAsociadoAsync(db, servicio, lote.TenantId, lote.PaqueteId, r, MotivoErrorTecnico, ct).ConfigureAwait(false);
            }
        }

        await TerminarSiCorrespondeAsync(db, r.LoteId, ct).ConfigureAwait(false);
        return true;
    }

    private async Task ProcesarAsync(IServiceProvider sp, ComparendosDb db, ServicioOcr servicio, Guid tenant, Guid paqueteId, DateTimeOffset solicitadoEn, Reclamo r, Actor actor,
        CancellationToken ct)
    {
        // Solo documentos del mismo paquete y la misma empresa del lote.
        var doc = await db.DocumentosOcr.FirstOrDefaultAsync(d => d.Id == r.DocumentoId && d.TenantId == tenant && d.PaqueteId == paqueteId, ct).ConfigureAwait(false);
        if (doc is null)
        {
            await CerrarAsync(db, r, ResultadoAsociacionLote.NoAsociado, MotivoCambio, null, ct).ConfigureAwait(false);
            return;
        }

        if (doc is { Estado: EstadoDocumentoOcr.Asociado, ComparendoId: { } comparendo })
        {
            // Retoma: la asociación quedó confirmada en una vuelta anterior que no llegó a registrar el correo.
            await RetomarCorreoAsync(sp, db, r, tenant, comparendo, doc.ArchivoId, solicitadoEn, actor, ct).ConfigureAwait(false);
            return;
        }

        if (doc.Estado != EstadoDocumentoOcr.PorAsociar)
        {
            await CerrarAsync(db, r, ResultadoAsociacionLote.NoAsociado, MotivoCambio, null, ct).ConfigureAwait(false);
            return;
        }

        var archivo = doc.ArchivoId;
        try
        {
            var (_, correo) = await sp.GetRequiredService<AsociacionDocumentos>()
                .UnoAsync(doc, reemplazar: false, evidenciaVista: null, actor, despachar: false, ct, desdeLote: true).ConfigureAwait(false);
            await CerrarAsync(db, r, ResultadoAsociacionLote.Asociado, null, correo, ct).ConfigureAwait(false);
        }
        catch (ReglaException ex)
        {
            db.ChangeTracker.Clear();
            var actual = await db.DocumentosOcr.AsNoTracking().FirstAsync(d => d.Id == r.DocumentoId, ct).ConfigureAwait(false);
            if (actual is { Estado: EstadoDocumentoOcr.Asociado, ComparendoId: { } ya })
                await RetomarCorreoAsync(sp, db, r, tenant, ya, archivo, solicitadoEn, actor, ct).ConfigureAwait(false); // commit en duda: sí quedó asociado
            else
                await NoAsociadoAsync(db, servicio, tenant, paqueteId, r, MotivoDelLote(ex), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Retoma una fila cuyo documento ya quedó asociado. Si la vuelta que se cayó alcanzó a registrar el correo (o la nota de que no
    /// salió) desde que se pidió el lote, solo se cierra la fila: así una caída o un apagado no mandan el documento dos veces.
    /// </summary>
    private async Task RetomarCorreoAsync(IServiceProvider sp, ComparendosDb db, Reclamo r, Guid tenant, Guid comparendo, Guid archivo, DateTimeOffset solicitadoEn, Actor actor,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var yaRegistrado = await (from c in db.Comunicaciones.AsNoTracking()
                                  join a in db.ComunicacionAdjuntos.AsNoTracking() on c.Id equals a.ComunicacionId
                                  where c.TenantId == tenant && c.ComparendoId == comparendo && c.Tipo == TipoComunicacion.Evidencia && a.ArchivoId == archivo && c.CreadoEn >= solicitadoEn
                                  select c.Id).AnyAsync(ct).ConfigureAwait(false);
        if (yaRegistrado)
        {
            await CerrarAsync(db, r, ResultadoAsociacionLote.Asociado, null, new ResultadoEvidencia(ResultadoEvidencia.Pendiente, null), ct).ConfigureAwait(false);
            return;
        }

        var nota = await db.Timeline.AsNoTracking()
            .Where(t => t.TenantId == tenant && t.ComparendoId == comparendo && t.Tipo == TipoEventoTimeline.EvidenciaOmitida && t.OcurridoEn >= solicitadoEn)
            .OrderByDescending(t => t.OcurridoEn).Select(t => t.Datos).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (nota is not null)
        {
            await CerrarAsync(db, r, ResultadoAsociacionLote.Asociado, null, new ResultadoEvidencia(ResultadoEvidencia.Omitido, MotivoDeNota(nota)), ct).ConfigureAwait(false);
            return;
        }

        var correo = await sp.GetRequiredService<ServicioEvidencia>().EnviarAlAsociarAsync(comparendo, tenant, archivo, false, actor, ct, despachar: false).ConfigureAwait(false);
        await CerrarAsync(db, r, ResultadoAsociacionLote.Asociado, null, correo, ct).ConfigureAwait(false);
    }

    private static string? MotivoDeNota(string datos)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(datos);
            return json.RootElement.TryGetProperty("motivo", out var m) ? m.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Devuelve el documento a Duplicado (si sigue PorAsociar), recalcula el paquete y cierra la fila con el motivo.</summary>
    private async Task NoAsociadoAsync(ComparendosDb db, ServicioOcr servicio, Guid tenant, Guid paqueteId, Reclamo r, string motivo, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await db.DocumentosOcr.Where(d => d.Id == r.DocumentoId && d.TenantId == tenant && d.Estado == EstadoDocumentoOcr.PorAsociar)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.Estado, EstadoDocumentoOcr.Duplicado).SetProperty(d => d.ProximoIntentoEn, (DateTimeOffset?)null), ct).ConfigureAwait(false);
        var paquete = await db.PaquetesOcr.FirstAsync(p => p.Id == paqueteId, ct).ConfigureAwait(false);
        await servicio.RecalcularAsync(paquete, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CerrarAsync(db, r, ResultadoAsociacionLote.NoAsociado, motivo, null, ct).ConfigureAwait(false);
    }

    /// <summary>Escribe el resultado solo si la fila sigue pendiente: una réplica con la concesión vencida no pisa a la otra.</summary>
    /// <remarks>
    /// Con plazo propio y no el del apagado del servicio: si el host empieza a detenerse justo aquí, la fila igual se cierra (si
    /// quedara abierta, la retoma tendría que reconocer el correo ya registrado; ver <see cref="RetomarCorreoAsync"/>).
    /// </remarks>
    private async Task<int> CerrarAsync(ComparendosDb db, Reclamo r, ResultadoAsociacionLote resultado, string? motivo, ResultadoEvidencia? correo, CancellationToken _)
    {
        using var propio = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (m, ce, cm) = (Recortar(motivo), correo?.Estado, Recortar(correo?.Motivo));
        var ahora = time.GetUtcNow();
        return await db.AsociacionLoteDocumentos.Where(i => i.LoteId == r.LoteId && i.DocumentoId == r.DocumentoId && i.Resultado == ResultadoAsociacionLote.PorAsociar)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.Resultado, resultado).SetProperty(i => i.Motivo, m).SetProperty(i => i.CorreoEstado, ce)
                .SetProperty(i => i.CorreoMotivo, cm).SetProperty(i => i.ProcesadoEn, ahora).SetProperty(i => i.ProximoIntentoEn, (DateTimeOffset?)null), propio.Token)
            .ConfigureAwait(false);
    }

    /// <summary>Después del commit de la fila: el último en terminar ve a los demás y cierra el lote.</summary>
    private async Task<int> TerminarSiCorrespondeAsync(ComparendosDb db, Guid loteId, CancellationToken _)
    {
        var ahora = time.GetUtcNow();
        using var propio = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE comparendos.asociacion_lote SET terminado_en = {ahora}
            WHERE id = {loteId} AND terminado_en IS NULL
              AND NOT EXISTS (SELECT 1 FROM comparendos.asociacion_lote_documento WHERE lote_id = {loteId} AND resultado = 'PorAsociar')
            """, propio.Token).ConfigureAwait(false);
    }

    private static string? Recortar(string? s) => s is { Length: > 500 } ? s[..500] : s;

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló la asociación en segundo plano de un documento escaneado.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}
