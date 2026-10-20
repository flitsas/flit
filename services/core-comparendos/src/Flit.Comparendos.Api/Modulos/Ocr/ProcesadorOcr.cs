using Flit.Comparendos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>
/// Cola de OCR (RN-052): reclama de a un documento PENDIENTE de un paquete EN_PROCESAMIENTO cuyo reintento ya venció,
/// con <c>FOR UPDATE SKIP LOCKED</c> (seguro con varias réplicas). El reclamo deja una concesión de 10 minutos: si la
/// réplica muere a mitad, el documento vuelve a la cola cuando vence.
/// </summary>
public sealed partial class ProcesadorOcr(IServiceScopeFactory scopes, TimeProvider time, ILogger<ProcesadorOcr> logger) : BackgroundService
{
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> ProcesarSiguienteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var ahora = time.GetUtcNow();
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE comparendos.documento_ocr SET estado = 'EnProcesamiento', proximo_intento_en = {ahora.AddMinutes(10)}
            WHERE id = (SELECT d.id FROM comparendos.documento_ocr d JOIN comparendos.paquete_ocr p ON p.id = d.paquete_id
                        WHERE p.estado = 'EnProcesamiento'
                          AND ((d.estado = 'Pendiente' AND (d.proximo_intento_en IS NULL OR d.proximo_intento_en <= {ahora}))
                               OR (d.estado = 'EnProcesamiento' AND d.proximo_intento_en <= {ahora}))
                        ORDER BY d.paquete_id, d.id LIMIT 1 FOR UPDATE OF d SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return false;

        var servicio = scope.ServiceProvider.GetRequiredService<ServicioOcr>();
        try
        {
            await servicio.ProcesarAsync(ids[0], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un error propio (no del proveedor) no puede dejar el documento colgado: queda FALLIDO y reintentable.
            LogFallo(logger, ex);
            db.ChangeTracker.Clear();
            await db.DocumentosOcr.Where(d => d.Id == ids[0]).ExecuteUpdateAsync(u => u
                .SetProperty(d => d.Estado, Domain.Modelo.EstadoDocumentoOcr.Fallido)
                .SetProperty(d => d.TipoFallo, Domain.Modelo.TipoFalloOcr.Red)
                .SetProperty(d => d.MensajeError, "Error interno al procesar el documento.")
                .SetProperty(d => d.ProximoIntentoEn, (DateTimeOffset?)null), ct).ConfigureAwait(false);
            var doc = await db.DocumentosOcr.AsNoTracking().FirstAsync(d => d.Id == ids[0], ct).ConfigureAwait(false);
            var paquete = await db.PaquetesOcr.FirstAsync(p => p.Id == doc.PaqueteId, ct).ConfigureAwait(false);
            await servicio.RecalcularAsync(paquete, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el procesamiento de un documento OCR.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}
