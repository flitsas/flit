using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Platform.Sdk.IaConsumo;

/// <summary>Persistencia del registro en la base del servicio. Cada servicio la resuelve sobre su propio DbContext.</summary>
public interface IIaConsumoAlmacen
{
    Task GuardarAsync(IReadOnlyList<IaConsumoPendiente> lote, CancellationToken ct);

    Task<IReadOnlyList<IaTarifaModelo>> TarifasAsync(CancellationToken ct);

    /// <summary>Borra el detalle con <c>fecha_inicio</c> anterior a <paramref name="limite"/>. Devuelve cuántas filas.</summary>
    Task<int> PurgarDetalleAsync(DateTimeOffset limite, CancellationToken ct);
}

/// <summary>
/// Almacén sobre el <typeparamref name="TContext"/> del servicio, que debe mapear las tablas con
/// <see cref="IaConsumoModelBuilderExtensions.AddFlitIaConsumo"/>. Un contexto nuevo por lote: no comparte el
/// change tracker con nadie.
/// </summary>
internal sealed class EfIaConsumoAlmacen<TContext>(TContext db) : IIaConsumoAlmacen
    where TContext : DbContext
{
    public async Task GuardarAsync(IReadOnlyList<IaConsumoPendiente> lote, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        foreach (var pendiente in lote)
        {
            db.Set<IaConsumoLog>().Add(pendiente.Fila);
            if (pendiente.Detalle is not null)
                db.Set<IaConsumoDetalle>().Add(pendiente.Detalle);
        }
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<IaTarifaModelo>> TarifasAsync(CancellationToken ct) =>
        await db.Set<IaTarifaModelo>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

    public Task<int> PurgarDetalleAsync(DateTimeOffset limite, CancellationToken ct) =>
        db.Set<IaConsumoDetalle>().Where(d => d.FechaInicio < limite).ExecuteDeleteAsync(ct);
}

/// <summary>
/// Costo estimado (contrato regla 9) con las tarifas cacheadas en memoria. Sin tarifa ⇒ null, nunca 0.
/// </summary>
public sealed partial class IaCalculadoraCosto(IaConsumoOpciones opciones, TimeProvider time) : IDisposable
{
    private readonly SemaphoreSlim _carga = new(1, 1);
    private IReadOnlyList<IaTarifaModelo> _tarifas = [];
    private DateTimeOffset _vence = DateTimeOffset.MinValue;

    public void Dispose() => _carga.Dispose();

    /// <summary>Tarifas vigentes en caché; si vencieron, se recargan con <paramref name="cargar"/>.</summary>
    public async Task<IReadOnlyList<IaTarifaModelo>> TarifasAsync(
        Func<CancellationToken, Task<IReadOnlyList<IaTarifaModelo>>> cargar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cargar);
        if (time.GetUtcNow() < _vence)
            return _tarifas;

        await _carga.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (time.GetUtcNow() < _vence)
                return _tarifas;
            try
            {
                _tarifas = await cargar(ct).ConfigureAwait(false);
                _vence = time.GetUtcNow() + opciones.CacheTarifas;
            }
            catch
            {
                // Sin tarifas el costo sale NULL; se reintenta en un minuto, no en cada lote.
                _vence = time.GetUtcNow() + TimeSpan.FromMinutes(1);
                throw;
            }
            return _tarifas;
        }
        finally
        {
            _carga.Release();
        }
    }

    /// <summary>
    /// Costo en USD de <paramref name="fila"/> con la tarifa vigente (<c>vigente_desde &lt;= fecha_inicio</c>, la más
    /// reciente) de (proveedor, modelo_respondido o, si es null, modelo_solicitado). Primero el id exacto, luego el id sin
    /// sufijo de fecha (<c>claude-haiku-4-5-20251001</c> → <c>claude-haiku-4-5</c>).
    /// </summary>
    public static decimal? Calcular(IReadOnlyList<IaTarifaModelo> tarifas, IaConsumoLog fila)
    {
        ArgumentNullException.ThrowIfNull(tarifas);
        ArgumentNullException.ThrowIfNull(fila);
        var modelo = fila.ModeloRespondido ?? fila.ModeloSolicitado;
        if (string.IsNullOrWhiteSpace(modelo))
            return null;

        var dia = DateOnly.FromDateTime(fila.FechaInicio.UtcDateTime);
        var tarifa = Vigente(tarifas, fila.Proveedor, modelo, dia);
        if (tarifa is null)
        {
            var sinFecha = SufijoFecha().Replace(modelo, string.Empty);
            if (!string.Equals(sinFecha, modelo, StringComparison.Ordinal))
                tarifa = Vigente(tarifas, fila.Proveedor, sinFecha, dia);
        }
        if (tarifa is null)
            return null;

        var costo = (fila.TokensEntrada * tarifa.PrecioEntradaMtok
                     + fila.TokensSalida * tarifa.PrecioSalidaMtok
                     + fila.TokensCacheLectura * tarifa.PrecioCacheLecturaMtok
                     + fila.TokensCacheEscritura * tarifa.PrecioCacheEscrituraMtok) / 1_000_000m;
        return Math.Round(costo, 6, MidpointRounding.AwayFromZero);
    }

    private static IaTarifaModelo? Vigente(IReadOnlyList<IaTarifaModelo> tarifas, string proveedor, string modelo, DateOnly dia) =>
        tarifas
            .Where(t => string.Equals(t.Proveedor, proveedor, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(t.Modelo, modelo, StringComparison.OrdinalIgnoreCase)
                        && t.VigenteDesde <= dia)
            .OrderByDescending(t => t.VigenteDesde)
            .FirstOrDefault();

    [GeneratedRegex(@"-\d{8}$")]
    private static partial Regex SufijoFecha();
}

/// <summary>
/// Saca de la cola y escribe por lotes (contrato regla 2). Si la base falla, advierte en el log, descarta el lote y sigue.
/// Al apagar vacía lo que quede con un tope de <see cref="IaConsumoOpciones.TopeVaciado"/>.
/// </summary>
internal sealed class IaConsumoEscritor(
    IaConsumoCola cola,
    IServiceScopeFactory scopes,
    IaCalculadoraCosto costo,
    IaConsumoOpciones opciones,
    ILogger<IaConsumoEscritor> logger) : BackgroundService
{
    /// <summary>Tope de cada escritura de lote, para que una base colgada no frene la cola indefinidamente.</summary>
    private static readonly TimeSpan TopeLote = TimeSpan.FromSeconds(30);

    // 0 = nadie ha leído la cola; 1 = la lee ExecuteAsync; 2 = la vació StopAsync. La cola es SingleReader, así que
    // solo uno de los dos puede leerla.
    private int _lector;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.CompareExchange(ref _lector, 1, 0) != 0)
            return;

        var lote = new List<IaConsumoPendiente>(opciones.TamanoLote);
        try
        {
            while (await cola.Lector.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                Tomar(lote);
                using var tope = new CancellationTokenSource(TopeLote);
                await EscribirAsync(lote, tope.Token).ConfigureAwait(false);
                lote.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await VaciarAsync(lote).ConfigureAwait(false);
    }

    /// <summary>
    /// Si el host se detiene antes de que <see cref="ExecuteAsync"/> llegue a correr (BackgroundService lo lanza en
    /// segundo plano y, con el token ya cancelado, puede no ejecutarlo nunca), la cola se vacía aquí.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (Interlocked.CompareExchange(ref _lector, 2, 0) == 0)
            await VaciarAsync(new List<IaConsumoPendiente>(opciones.TamanoLote)).ConfigureAwait(false);
    }

    /// <summary>Al apagar: lo que quede en la cola, en lotes, hasta el tope.</summary>
    internal async Task VaciarAsync(List<IaConsumoPendiente> lote)
    {
        using var tope = new CancellationTokenSource(opciones.TopeVaciado);
        Tomar(lote);
        while (lote.Count > 0 && !tope.IsCancellationRequested)
        {
            await EscribirAsync(lote, tope.Token).ConfigureAwait(false);
            lote.Clear();
            Tomar(lote);
        }

        var quedan = lote.Count;
        while (cola.Lector.TryRead(out _))
            quedan++;
        if (quedan > 0)
            IaConsumoBitacora.VaciadoIncompleto(logger, quedan);
    }

    private void Tomar(List<IaConsumoPendiente> lote)
    {
        while (lote.Count < opciones.TamanoLote && cola.Lector.TryRead(out var pendiente))
            lote.Add(pendiente);
    }

    /// <summary>Calcula el costo y guarda el lote. Nunca lanza.</summary>
    internal async Task EscribirAsync(List<IaConsumoPendiente> lote, CancellationToken ct)
    {
        if (lote.Count == 0)
            return;
#pragma warning disable CA1031 // Un fallo de la base nunca tumba el escritor ni la llamada a la IA.
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var almacen = scope.ServiceProvider.GetRequiredService<IIaConsumoAlmacen>();

            IReadOnlyList<IaTarifaModelo> tarifas = [];
            try
            {
                tarifas = await costo.TarifasAsync(almacen.TarifasAsync, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                IaConsumoBitacora.TarifasNoLeidas(logger, ex.GetType().Name);
            }

            foreach (var pendiente in lote)
                pendiente.Fila.CostoUsdEstimado = IaCalculadoraCosto.Calcular(tarifas, pendiente.Fila);

            await almacen.GuardarAsync(lote, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IaConsumoBitacora.LoteNoGuardado(logger, lote.Count, ex.GetType().Name, ex);
        }
#pragma warning restore CA1031
    }
}

/// <summary>Retención del detalle (contrato): una vez al día borra el detalle de más de N días.</summary>
internal sealed class IaConsumoRetencion(
    IServiceScopeFactory scopes,
    IaConsumoOpciones opciones,
    TimeProvider time,
    ILogger<IaConsumoRetencion> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromDays(1);
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, time, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                await PurgarAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Intervalo, time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task<int> PurgarAsync(CancellationToken ct)
    {
#pragma warning disable CA1031 // La purga falla en silencio (log) y se reintenta mañana.
        try
        {
            var limite = time.GetUtcNow() - TimeSpan.FromDays(opciones.DiasDetalle);
            await using var scope = scopes.CreateAsyncScope();
            var almacen = scope.ServiceProvider.GetRequiredService<IIaConsumoAlmacen>();
            var borradas = await almacen.PurgarDetalleAsync(limite, ct).ConfigureAwait(false);
            if (borradas > 0)
                IaConsumoBitacora.DetallePurgado(logger, limite, borradas);
            return borradas;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            IaConsumoBitacora.PurgaFallo(logger, ex.GetType().Name);
            return 0;
        }
#pragma warning restore CA1031
    }
}
