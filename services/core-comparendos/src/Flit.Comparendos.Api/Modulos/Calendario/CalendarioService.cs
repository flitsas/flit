using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Domain.Calendario;
using Flit.Comparendos.Domain.Plazos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Calendario;

/// <summary>
/// Calendario hábil vigente, leído de <c>calendario_dia</c> (lo que el SuperAdmin corrigió gana sobre el cálculo) y
/// guardado en memoria. Al corregir un festivo se invalida y la siguiente lectura lo vuelve a cargar.
/// </summary>
public sealed class CalendarioService(IServiceScopeFactory scopes) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CalendarioHabil? _actual;

    public async Task<CalendarioHabil> ActualAsync(CancellationToken ct)
    {
        if (_actual is { } listo)
            return listo;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_actual is { } otro)
                return otro;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            var festivos = await db.CalendarioDias.AsNoTracking().Where(d => d.EsFestivo).Select(d => d.Fecha).ToListAsync(ct).ConfigureAwait(false);
            _actual = festivos.Count > 0 ? new CalendarioHabil(festivos) : CalendarioHabil.Calculado(2015, 2040);
            return _actual;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MotorDePlazos> MotorAsync(CancellationToken ct) => new(await ActualAsync(ct).ConfigureAwait(false));

    public void Invalidar() => _actual = null;

    public void Dispose() => _gate.Dispose();
}
