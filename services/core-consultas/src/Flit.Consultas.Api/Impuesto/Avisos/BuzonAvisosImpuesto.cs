using System.Collections.Concurrent;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// Resultado de un aviso ya verificado (firma válida, empresa y consulta propias): el <c>consulta</c> del cuerpo tal como llegó, que
/// tiene la misma forma que el <c>GET /consultas/{id}</c> del polling. Lleva datos personales: <see cref="ToString"/> no los muestra.
/// </summary>
internal sealed record AvisoImpuestoVerificado(int Attempt, EstadoTrabajoExterno Estado, string CuerpoConsulta, DateTimeOffset RecibidoEn)
{
    public override string ToString() => $"AvisoImpuestoVerificado {{ Attempt = {Attempt}, Estado = {Estado} }}";
}

/// <summary>
/// Buzón en memoria entre el receptor del aviso y el sondeo que espera esa consulta (HU #14711, plan §3.3). El receptor deja el aviso
/// (<see cref="Entregar"/>, se queda con el de mayor <c>attempt</c>) y despierta a quien espera; el sondeo, en lugar de dormir el
/// intervalo, espera en el buzón (<see cref="EsperarAsync"/>) y, si llega el aviso, cierra la consulta con él por el mismo camino del
/// polling y sin el GET. Así hay un solo cierre y una sola publicación a Diagnóstico por ejecución del trabajo. Lo que no se recoge
/// vence a los <see cref="Retencion"/> y el buzón no pasa de <see cref="Capacidad"/> consultas. En memoria a propósito: el resultado
/// tiene datos personales y no se persiste; si el aviso llega a otra réplica o el servicio se reinicia, el polling cierra igual.
/// La retención es el presupuesto máximo del sondeo (<see cref="OpcionesImpuesto.TimeoutSegundos"/>) más
/// <see cref="MargenRetencion"/> (N-1): pasado ese tiempo ningún sondeo sigue esperando esa consulta y el aviso solo ocuparía memoria
/// con datos personales.
/// </summary>
/// <example>
/// <code>
/// var buzon = new BuzonAvisosImpuesto(time, BuzonAvisosImpuesto.RetencionPara(opciones.TimeoutSegundos));
/// buzon.Entregar(tenantId, "ext-1", aviso);                              // receptor
/// var aviso = await buzon.EsperarAsync(tenantId, "ext-1", intervalo, ct); // sondeo: null = no llegó, hacer el GET
/// </code>
/// </example>
internal sealed class BuzonAvisosImpuesto(TimeProvider time, TimeSpan retencion)
{
    /// <summary>Holgura sobre el presupuesto del sondeo: cubre el último GET en vuelo y el desfase entre réplicas.</summary>
    public static readonly TimeSpan MargenRetencion = TimeSpan.FromSeconds(60);
    public const int Capacidad = 10_000;

    /// <summary>Con el presupuesto por defecto (<see cref="OpcionesImpuesto.TimeoutPorDefecto"/>); pruebas y hosts sin opciones.</summary>
    public BuzonAvisosImpuesto(TimeProvider time)
        : this(time, RetencionPara(OpcionesImpuesto.TimeoutPorDefecto))
    {
    }

    /// <summary>Presupuesto total del sondeo en segundos más <see cref="MargenRetencion"/>.</summary>
    public static TimeSpan RetencionPara(int timeoutSegundos)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutSegundos);
        return TimeSpan.FromSeconds(timeoutSegundos) + MargenRetencion;
    }

    public TimeSpan Retencion { get; } = retencion > TimeSpan.Zero
        ? retencion
        : throw new ArgumentOutOfRangeException(nameof(retencion), "La retención del buzón debe ser positiva.");

    private readonly ConcurrentDictionary<(Guid, string), (AvisoImpuestoVerificado Aviso, DateTimeOffset Entregado)> _avisos = new();
    private readonly ConcurrentDictionary<(Guid, string), TaskCompletionSource> _esperas = new();
    private readonly object _candado = new();

    /// <summary>Deja el aviso de la consulta <paramref name="idExterno"/> de la empresa; uno de menor o igual attempt no reemplaza.</summary>
    public void Entregar(Guid tenantId, string idExterno, AvisoImpuestoVerificado aviso)
    {
        ArgumentException.ThrowIfNullOrEmpty(idExterno);
        ArgumentNullException.ThrowIfNull(aviso);
        var clave = (tenantId, idExterno);
        lock (_candado)
        {
            Depurar();
            if (_avisos.TryGetValue(clave, out var actual) && !Vencido(actual.Entregado) && actual.Aviso.Attempt >= aviso.Attempt)
                return;
            if (!_avisos.ContainsKey(clave) && _avisos.Count >= Capacidad)
                return;
            _avisos[clave] = (aviso, time.GetUtcNow());
        }

        if (_esperas.TryGetValue(clave, out var espera))
            espera.TrySetResult();
    }

    /// <summary>Saca el aviso si ya está; si no, espera hasta <paramref name="espera"/> a que llegue. Null si no llegó.</summary>
    public async Task<AvisoImpuestoVerificado?> EsperarAsync(Guid tenantId, string idExterno, TimeSpan espera, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(idExterno);
        var clave = (tenantId, idExterno);
        if (Sacar(clave) is { } listo)
            return listo;
        if (espera <= TimeSpan.Zero)
            return null;

        var senal = _esperas.GetOrAdd(clave, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            // Pudo llegar entre el primer intento y el registro de la espera.
            if (Sacar(clave) is { } entreTanto)
                return entreTanto;
            using var corte = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await Task.WhenAny(senal.Task, Task.Delay(espera, time, corte.Token)).ConfigureAwait(false);
            await corte.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return Sacar(clave);
        }
        finally
        {
            _esperas.TryRemove(new KeyValuePair<(Guid, string), TaskCompletionSource>(clave, senal));
        }
    }

    /// <summary>Descarta lo que quede de la consulta (ya cerrada).</summary>
    public void Olvidar(Guid tenantId, string? idExterno)
    {
        if (!string.IsNullOrEmpty(idExterno))
            _avisos.TryRemove((tenantId, idExterno), out _);
    }

    private AvisoImpuestoVerificado? Sacar((Guid, string) clave)
    {
        if (!_avisos.TryRemove(clave, out var entrada))
            return null;
        return Vencido(entrada.Entregado) ? null : entrada.Aviso;
    }

    private bool Vencido(DateTimeOffset entregado) => time.GetUtcNow() - entregado > Retencion;

    private void Depurar()
    {
        if (_avisos.Count < Capacidad / 2)
            return;
        foreach (var (clave, entrada) in _avisos)
        {
            if (Vencido(entrada.Entregado))
                _avisos.TryRemove(clave, out _);
        }
    }
}
