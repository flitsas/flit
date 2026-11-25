using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Resultados;

/// <summary>Qué pasó al aplicar un resultado (se guarda en <c>avisos_recibidos.resultado</c> con su código en mayúsculas).</summary>
public enum Aplicacion
{
    /// <summary>El ítem cambió y el lote se recalculó.</summary>
    Aplicado,

    /// <summary><c>attempt</c> menor que el mayor ya recibido: se registra y no se aplica (plan §3.2).</summary>
    AttemptMenor,

    /// <summary>El mismo <c>attempt</c> ya estaba aplicado (por ejemplo, el GET después del webhook).</summary>
    SinCambio,

    /// <summary>El <c>consulta.id</c> no es el del ítem (y el ítem no está en <c>INCIERTO</c>): se descarta.</summary>
    ConsultaDistinta,

    /// <summary>La consulta sigue en curso (<c>queued</c> o <c>running</c>).</summary>
    NoFinal,

    /// <summary>El estado del ítem no admite un resultado (fuera de la tabla del dominio): queda para la conciliación.</summary>
    NoAplicable,

    /// <summary>El ítem no existe en la compañía.</summary>
    NoEncontrado,
}

/// <summary>
/// Aplica un resultado del proveedor (webhook o GET) a su ítem y recalcula el lote (plan §3.2–§3.3, F2b-CF2/CF6/CF7), en una sola
/// transacción con la cabecera del lote bloqueada (<c>FOR UPDATE</c>), así que dos ítems del mismo lote no se pisan los
/// contadores. Reglas:
/// <list type="bullet">
///   <item>El <c>consulta.id</c> debe ser el del ítem; un ítem <c>INCIERTO</c> sin id lo adopta.</item>
///   <item>Manda el mayor <c>attempt</c>: uno menor se descarta; uno mayor reemplaza un resultado ya final (sin reabrir nada).</item>
///   <item>La transición la decide <see cref="TransicionesItem"/>: <c>EXITOSO</c> (con <c>parcial</c>), <c>ERROR</c> con la clase de
///   la tabla §9, <c>REINTENTO_PROGRAMADO</c> por <c>rate_limited</c> (consulta nueva con token nuevo, esperas DA-5) o resultado
///   tardío sobre <c>SIN_RESULTADO_LOTE_CERRADO</c>, que no reabre el lote.</item>
///   <item>Montos sin PII en columnas (null no es 0) y el cuerpo completo de la consulta cifrado (<c>validations</c> puede traer
///   datos del propietario, DA-7); las vigencias se reemplazan.</item>
///   <item>Un éxito con archivos deja <see cref="TipoRecibos"/> en la outbox (lo consume M11).</item>
/// </list>
/// Fija la compañía en el scope (<see cref="EmpresaDelTrabajo"/>): un scope por compañía. No registra montos, placa ni cuerpo.
/// </summary>
/// <example>
/// <code>
/// var r = await aplicador.AplicarAsync(empresa, itemId, LectorResultado.DesdeConsulta(json)!, ct); // Aplicacion.Aplicado
/// </code>
/// </example>
public sealed partial class AplicadorResultado(
    ImpuestosDb db, EmpresaDelTrabajo empresaDelTrabajo, IPlatformOutbox outbox, IProteccionImpuestos proteccion, TimeProvider reloj, ILogger<AplicadorResultado> log)
{
    /// <summary>Evento interno de M11 (asyncapi <c>impuestos-events.v1.yaml</c>): solo el id del ítem.</summary>
    public const string TipoRecibos = "impuestos.recibos.descargar";

    /// <summary>Esperas del reenvío por <c>rate_limited</c> (DA-5): 30 s, 2 min y 10 min.</summary>
    public static IReadOnlyList<TimeSpan> EsperasReenvio { get; } = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    private const int IntentosPorConcurrencia = 3;

    public Task<Aplicacion> AplicarAsync(Guid empresa, Guid itemId, ResultadoLeido resultado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return EnLoteAsync(empresa, itemId, (item, ahora) => AplicarEnItemAsync(empresa, item, resultado, ahora, ct), ct);
    }

    /// <summary>
    /// Aplica <paramref name="paso"/> al ítem dentro de la transacción del lote y, si devuelve <see cref="Aplicacion.Aplicado"/>,
    /// recalcula los contadores y el estado del lote y guarda todo junto; si no, no guarda nada.
    /// </summary>
    internal Task<Aplicacion> TransicionarAsync(Guid empresa, Guid itemId, Func<LiquidacionItem, DateTimeOffset, Aplicacion> paso, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paso);
        return EnLoteAsync(empresa, itemId, (item, ahora) => Task.FromResult(paso(item, ahora)), ct);
    }

    private async Task<Aplicacion> EnLoteAsync(Guid empresa, Guid itemId, Func<LiquidacionItem, DateTimeOffset, Task<Aplicacion>> paso, CancellationToken ct)
    {
        empresaDelTrabajo.Fijar(empresa);
        var loteId = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => (Guid?)i.LiquidacionId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (loteId is not { } lote)
            return Aplicacion.NoEncontrado;

        for (var intento = 1; ; intento++)
        {
            try
            {
                return await db.Database.CreateExecutionStrategy().ExecuteAsync(() => EnTransaccionAsync(itemId, lote, paso, ct)).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (intento < IntentosPorConcurrencia)
            {
                Reintento(log, itemId, intento);
            }
        }
    }

    private async Task<Aplicacion> EnTransaccionAsync(Guid itemId, Guid loteId, Func<LiquidacionItem, DateTimeOffset, Task<Aplicacion>> paso, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Un aplicador por lote a la vez: la cabecera queda bloqueada hasta el commit (los contadores se recalculan con lo confirmado).
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM impuestos.liquidaciones WHERE id = {loteId} FOR UPDATE", ct).ConfigureAwait(false);
        var item = await db.Set<LiquidacionItem>().SingleAsync(i => i.Id == itemId, ct).ConfigureAwait(false);
        var ahora = reloj.GetUtcNow();
        var resultado = await paso(item, ahora).ConfigureAwait(false);
        if (resultado != Aplicacion.Aplicado)
            return resultado;

        var lote = await db.Set<Liquidacion>().SingleAsync(l => l.Id == loteId, ct).ConfigureAwait(false);
        var estados = await db.Set<LiquidacionItem>().AsNoTracking()
            .Where(i => i.LiquidacionId == loteId && i.Id != itemId)
            .Select(i => new EstadoParaContar(i.Estado, i.ResultadoStatus))
            .ToListAsync(ct).ConfigureAwait(false);
        estados.Add(new EstadoParaContar(item.Estado, item.ResultadoStatus));
        var anterior = ContadoresLote.Recalcular(lote, estados, ahora);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        Aplicado(log, item.Id, item.Estado, lote.Id, anterior, lote.Estado);
        return resultado;
    }

    private async Task<Aplicacion> AplicarEnItemAsync(Guid empresa, LiquidacionItem item, ResultadoLeido r, DateTimeOffset ahora, CancellationToken ct)
    {
        if (!string.Equals(item.ConsultaApiId, r.ConsultaId, StringComparison.Ordinal))
        {
            if (item.ConsultaApiId is not null)
                return Aplicacion.ConsultaDistinta;
            if (item.Estado != EstadoItem.Incierto)
                return Aplicacion.NoAplicable;
        }

        if (!r.EsFinal)
            return Aplicacion.NoFinal;
        if (!TransicionesItem.AplicaAviso(r.Attempt, item.AttemptApiMax))
            return Aplicacion.AttemptMenor;

        // Un attempt mayor sobre un resultado ya final lo reemplaza como uno tardío: termina en EXITOSO o ERROR, nunca reenvía.
        var reemplaza = item.Estado is EstadoItem.Exitoso or EstadoItem.Error;
        if (reemplaza && r.Attempt <= item.AttemptApiMax)
            return Aplicacion.SinCambio;
        var desde = reemplaza ? EstadoItem.SinResultadoLoteCerrado : item.Estado;

        ResultadoItem t;
        try
        {
            t = TransicionesItem.Aplicar(
                new SituacionItem(desde, item.IntentosRunt, item.IntentosReenvio, item.ErrorClase), new EventoItem.Final(r.Status, r.ErrorCodigo));
        }
        catch (TransicionInvalida)
        {
            return Aplicacion.NoAplicable;
        }

        item.Estado = t.Nueva.Estado;
        item.IntentosReenvio = t.Nueva.IntentosReenvio;
        item.ConsultaStatus = Recortar(r.Status, 12);
        item.ActualizadoEn = ahora;
        item.ProximoPollEn = null;
        if (t.TokenNuevo)
        {
            // rate_limited: la consulta nueva lleva token nuevo (lo genera M09 al reenviar); esta queda descartada.
            item.TokenHash = null;
            item.ConsultaApiId = null;
            item.ConCallback = false;
            item.AttemptApiMax = 0;
            item.ProximoIntentoEn = ahora + EsperasReenvio[Math.Clamp(item.IntentosReenvio - 1, 0, EsperasReenvio.Count - 1)];
            return Aplicacion.Aplicado;
        }

        var exitoso = item.Estado == EstadoItem.Exitoso;
        item.ConsultaApiId = r.ConsultaId;
        item.AttemptApiMax = r.Attempt;
        item.ProximoIntentoEn = null;
        item.ErrorClase = exitoso ? null : t.Nueva.ClaseError;
        item.ErrorCodigo = exitoso ? null : Recortar(t.ErrorCodigo, 48);
        item.ErrorMensaje = exitoso ? null : Recortar(r.ErrorMensaje, 500);
        item.Parcial = t.Parcial;
        if (!reemplaza)
            item.ResultadoTardio |= t.ResultadoTardio;
        item.ResultadoStatus = Recortar(r.ResultadoStatus, 12);
        item.TotalAPagar = exitoso ? r.TotalAPagar : null;
        item.Warnings = [.. r.Warnings];
        item.ResultadoCifrado = proteccion.ProtegerResultado(r.ConsultaJson);
        item.DepartamentoCodigo ??= Recortar(r.DepartamentoCodigo, 2);
        item.DepartamentoNombre ??= Recortar(r.DepartamentoNombre, 80);

        var anteriores = await db.Set<LiquidacionPeriodo>().Where(p => p.ItemId == item.Id).ToListAsync(ct).ConfigureAwait(false);
        db.RemoveRange(anteriores);
        if (exitoso)
        {
            foreach (var p in r.Periodos)
            {
                db.Add(new LiquidacionPeriodo
                {
                    ItemId = item.Id,
                    TenantId = item.TenantId,
                    Periodo = Recortar(p.Periodo, 9) ?? string.Empty,
                    Avaluo = p.Avaluo,
                    Impuesto = p.Impuesto,
                    Sancion = p.Sancion,
                    Intereses = p.Intereses,
                    Descuentos = p.Descuentos,
                    PagosPrevios = p.PagosPrevios,
                    TotalAPagar = p.TotalAPagar,
                    OtrosCargos = p.OtrosCargosJson,
                    Vencimiento = p.Vencimiento,
                    ReferenciaPago = Recortar(p.ReferenciaPago, 80),
                    FileIdApi = Recortar(p.FileId, 64),
                    NoLiquidable = p.NoLiquidable,
                });
            }

            if (r.TieneArchivos)
                outbox.Enqueue(TipoRecibos, 1, empresa, new { itemId = item.Id });
        }

        return Aplicacion.Aplicado;
    }

    private static string? Recortar(string? valor, int largo) => valor is null || valor.Length <= largo ? valor : valor[..largo];

    [LoggerMessage(Level = LogLevel.Information, Message = "Resultado aplicado al ítem {Item}: {Estado}; lote {Lote} de {Antes} a {Despues}.")]
    private static partial void Aplicado(ILogger logger, Guid item, EstadoItem estado, Guid lote, EstadoLote antes, EstadoLote despues);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conflicto de concurrencia al aplicar el resultado del ítem {Item} (intento {Intento}); se reintenta.")]
    private static partial void Reintento(ILogger logger, Guid item, int intento);
}
