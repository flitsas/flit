using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Correo;

/// <summary>
/// Quien necesita saber cómo terminó un correo (el DP pasa a ENVIADO, la solicitud de cambio se confirma…). Se llama
/// en el mismo scope y antes del SaveChanges que guarda el nuevo estado.
/// </summary>
public interface IReaccionCorreo
{
    Task AlTerminarAsync(ComunicacionSaliente comunicacion, ResultadoEnvioCorreo resultado, CancellationToken ct);
}

/// <summary>
/// Envía lo PENDIENTE del Centro de Mensajes (§6.14). Reclama hasta 20 correos con <c>FOR UPDATE SKIP LOCKED</c> (seguro
/// con varias réplicas), los entrega al transporte y guarda cada intento. Transitorio: hasta 3 intentos a 1, 5 y 25
/// minutos; rebote: REBOTADO sin reintento; definitivo: FALLIDO.
/// </summary>
public sealed partial class DespachadorCorreos(IServiceScopeFactory scopes, TimeProvider time, ILogger<DespachadorCorreos> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(25)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DespacharPendientesAsync(stoppingToken).ConfigureAwait(false) > 0)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), time, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Un lote de pendientes vencidos. Devuelve cuántos trató.</summary>
    public async Task<int> DespacharPendientesAsync(CancellationToken ct)
    {
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            var ahora = time.GetUtcNow();
            ids = await db.Database.SqlQuery<Guid>($"""
                UPDATE comparendos.comunicacion_saliente SET proximo_intento_en = {ahora.AddMinutes(10)}
                WHERE id IN (SELECT id FROM comparendos.comunicacion_saliente
                             WHERE estado = 'Pendiente' AND (proximo_intento_en IS NULL OR proximo_intento_en <= {ahora})
                             ORDER BY creado_en LIMIT 20 FOR UPDATE SKIP LOCKED)
                RETURNING id AS "Value"
                """).ToListAsync(ct).ConfigureAwait(false);
        }

        foreach (var id in ids)
            await EnviarReclamadoAsync(id, ct).ConfigureAwait(false);
        return ids.Count;
    }

    /// <summary>
    /// Envía ya un correo concreto (por ejemplo, el DP al pulsar «Enviar») sin esperar al ciclo. Antes lo reclama con un
    /// UPDATE condicional (la misma concesión de 10 minutos que deja el ciclo): si el ciclo u otra llamada ya lo tomó, no lo
    /// vuelve a enviar y devuelve el estado que tenga (bug #14270: correos duplicados del cambio de contraventor).
    /// </summary>
    public async Task<EstadoComunicacion> DespacharAsync(Guid id, CancellationToken ct)
    {
        await using (var reclamo = scopes.CreateAsyncScope())
        {
            var db = reclamo.ServiceProvider.GetRequiredService<ComparendosDb>();
            var ahora = time.GetUtcNow();
            var reclamado = await db.Database.SqlQuery<Guid>($"""
                UPDATE comparendos.comunicacion_saliente SET proximo_intento_en = {ahora.AddMinutes(10)}
                WHERE id = {id} AND estado = 'Pendiente' AND (proximo_intento_en IS NULL OR proximo_intento_en <= {ahora})
                RETURNING id AS "Value"
                """).ToListAsync(ct).ConfigureAwait(false);
            if (reclamado.Count == 0)
                return await EstadoAsync(db, id, ct).ConfigureAwait(false);
        }

        return await EnviarReclamadoAsync(id, ct).ConfigureAwait(false);
    }

    private static async Task<EstadoComunicacion> EstadoAsync(ComparendosDb db, Guid id, CancellationToken ct) =>
        await db.Comunicaciones.AsNoTracking().Where(x => x.Id == id).Select(x => (EstadoComunicacion?)x.Estado).FirstOrDefaultAsync(ct).ConfigureAwait(false)
        ?? EstadoComunicacion.Fallido;

    /// <summary>Envía un correo que ya se reclamó (el ciclo o <see cref="DespacharAsync"/>).</summary>
    private async Task<EstadoComunicacion> EnviarReclamadoAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var c = await db.Comunicaciones.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (c is null || c.Estado != EstadoComunicacion.Pendiente)
            return c?.Estado ?? EstadoComunicacion.Fallido;

        var almacen = scope.ServiceProvider.GetRequiredService<IAlmacenArchivos>();
        var envio = scope.ServiceProvider.GetRequiredService<IEnvioCorreo>();
        var correo = await ArmarAsync(db, almacen, c, ct).ConfigureAwait(false);
        var reloj = Stopwatch.StartNew();
        ResultadoEnvioCorreo r;
        try
        {
            r = await envio.EnviarAsync(c.TenantId, correo, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            r = new ResultadoEnvioCorreo("PROVEEDOR_NO_DISPONIBLE", ex.Message, false, null);
        }

        var ahora = time.GetUtcNow();
        c.CantidadReintentos++;
        db.ComunicacionIntentos.Add(new ComunicacionIntento
        {
            Id = Guid.CreateVersion7(),
            ComunicacionId = c.Id,
            Numero = c.CantidadReintentos,
            ResultadoPlataforma = r.Resultado,
            Mensaje = r.Mensaje is { Length: > 2000 } m ? m[..2000] : r.Mensaje,
            DuracionMs = (int)reloj.ElapsedMilliseconds,
            IntentadoEn = ahora,
        });
        c.ResultadoPlataforma = r.Resultado;
        c.EntregaIdPlataforma = r.EntregaId ?? c.EntregaIdPlataforma;
        c.Desviado = r.Desviado;
        if (r.Exito)
        {
            c.Estado = EstadoComunicacion.Enviado;
            c.EnviadoEn = ahora;
            c.ProximoIntentoEn = null;
            c.MotivoFallo = null;
        }
        // RN-122: hasta 3 reintentos (1, 5 y 25 min) después del primer intento.
        else if (r.Transitorio && c.CantidadReintentos <= Esperas.Length)
        {
            c.ProximoIntentoEn = ahora + Esperas[c.CantidadReintentos - 1];
            c.MotivoFallo = r.Mensaje;
        }
        else
        {
            c.Estado = r.Rebote ? EstadoComunicacion.Rebotado : EstadoComunicacion.Fallido;
            c.ProximoIntentoEn = null;
            c.MotivoFallo = r.Mensaje ?? r.Resultado;
        }

        if (c.Estado == EstadoComunicacion.Pendiente)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return c.Estado;
        }

        // El estado final y lo que crean las reacciones (el aviso 4, el DP enviado…) se guardan en una transacción que
        // bloquea la fila del correo: si otro camino ya lo cerró, no se reacciona dos veces. La estrategia de reintentos
        // de Npgsql exige que la transacción corra dentro de ella.
        var cerrado = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var sigue = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM comparendos.comunicacion_saliente WHERE id = {c.Id} AND estado = 'Pendiente' FOR UPDATE
                """).ToListAsync(ct).ConfigureAwait(false);
            if (sigue.Count == 0)
                return false;
            foreach (var reaccion in scope.ServiceProvider.GetServices<IReaccionCorreo>())
                await reaccion.AlTerminarAsync(c, r, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        if (cerrado)
            return c.Estado;
        db.ChangeTracker.Clear();
        return await EstadoAsync(db, id, ct).ConfigureAwait(false);
    }

    private static async Task<CorreoParaEnviar> ArmarAsync(ComparendosDb db, IAlmacenArchivos almacen, ComunicacionSaliente c, CancellationToken ct)
    {
        var html = await Leer(db, almacen, c.CuerpoHtmlArchivoId, ct).ConfigureAwait(false);
        var adjuntos = new List<AdjuntoCorreo>();
        foreach (var a in await db.ComunicacionAdjuntos.AsNoTracking().Where(x => x.ComunicacionId == c.Id).ToListAsync(ct).ConfigureAwait(false))
        {
            var archivo = await db.Archivos.AsNoTracking().FirstAsync(x => x.Id == a.ArchivoId, ct).ConfigureAwait(false);
            var bytes = await almacen.LeerAsync(archivo.Referencia, ct).ConfigureAwait(false) ?? [];
            adjuntos.Add(new AdjuntoCorreo(a.Nombre, archivo.ContentType, bytes, archivo.Id));
        }

        using var destinatarios = JsonDocument.Parse(c.Destinatarios);
        var para = destinatarios.RootElement.GetProperty("para")[0];
        var bcc = destinatarios.RootElement.TryGetProperty("bcc", out var b) ? b.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        return new CorreoParaEnviar(
            c.PlantillaClave.Length > 0 ? c.PlantillaClave : $"comparendos.{c.Tipo}",
            c.CanalCorreo ?? CanalCorreo.FlitSmtp,
            para.GetProperty("email").GetString()!,
            para.TryGetProperty("nombre", out var n) ? n.GetString() : null,
            c.Asunto,
            Encoding.UTF8.GetString(html),
            adjuntos,
            bcc,
            c.RemitenteNombre);
    }

    private static async Task<byte[]> Leer(ComparendosDb db, IAlmacenArchivos almacen, Guid archivoId, CancellationToken ct)
    {
        var referencia = await db.Archivos.AsNoTracking().Where(a => a.Id == archivoId).Select(a => a.Referencia).FirstAsync(ct).ConfigureAwait(false);
        return await almacen.LeerAsync(referencia, ct).ConfigureAwait(false) ?? [];
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el despacho de correos de Comparendos.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}
