using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Reintento programado de un ítem del masivo (Punto 4, incremento 3, diseño §5.3 punto 5; patrón <see cref="ProgramadorReintentos"/>
/// de ADR-0075). Ante un transitorio con intentos disponibles:
/// <list type="number">
/// <item><see cref="ProgramarAsync"/>: en un scope PROPIO (lo del mensaje se revierte al lanzar) deja el ítem ENVIADO con
/// <c>intentos</c> y <c>disponible_desde = ahora + espera(n)</c>: el ítem CONSERVA su crédito, así la compañía no inunda mientras
/// Trámites sufre.</item>
/// <item>Lanza <see cref="ReintentoProgramadoException"/>: el SDK lleva el mensaje a <c>diagnostico.derivaciones.retry.n</c>
/// (<see cref="Esperas"/>: 30 s, 2 min, 10 min); al volver, el consumidor lo atiende si la espera venció o le da otra vuelta.</item>
/// </list>
/// Agotado <see cref="OpcionesDerivacion.MaxIntentos"/>, el consumidor cierra el ítem FALLIDO (reintentable a mano). Logs: ids.
/// </summary>
internal sealed partial class ReintentoDerivacion(IServiceScopeFactory scopes, TimeProvider reloj, ILogger<ReintentoDerivacion> log)
{
    /// <summary>Esperas de las colas <c>.retry.n</c> del consumidor (TTL de cada cola, declarado por el SDK).</summary>
    public static IReadOnlyList<TimeSpan> Esperas { get; } = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    /// <summary>Holgura al comparar con <c>disponible_desde</c>: el TTL del broker no es exacto al milisegundo.</summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Cuándo se puede volver a llamar tras <paramref name="intentos"/> llamadas fallidas; null si se agotaron (<paramref name="maxIntentos"/>).
    /// </summary>
    public static DateTimeOffset? Siguiente(int intentos, int maxIntentos, DateTimeOffset ahora) =>
        intentos >= maxIntentos || intentos < 1 ? null : ahora + Esperas[Math.Min(intentos, Esperas.Count) - 1];

    /// <summary>
    /// Deja el ítem con su reintento (scope propio, empresa fijada) y lanza <see cref="ReintentoProgramadoException"/>. Si el ítem ya
    /// no está ENVIADO (lo cerró otro) no cambia nada y no lanza.
    /// </summary>
    public async Task ProgramarAsync(Guid empresa, Guid itemId, short intentos, DateTimeOffset disponibleDesde, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var programado = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var item = await db.Set<ItemDerivacion>().SingleOrDefaultAsync(i => i.Id == itemId, ct).ConfigureAwait(false);
            if (item is null || item.Estado != EstadoItemDerivacion.Enviado)
                return false;
            item.Intentos = intentos;
            item.DisponibleDesde = disponibleDesde;
            item.ActualizadoEn = reloj.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }

            return true;
        }).ConfigureAwait(false);

        if (!programado)
        {
            NoProgramado(log, itemId);
            return;
        }

        Programado(log, itemId, intentos, disponibleDesde);
        throw new ReintentoProgramadoException("Reintento programado del ítem de derivación: el mensaje espera en la cola de reintentos.");
    }

    [LoggerMessage(EventId = 84320, Level = LogLevel.Information, Message = "Ítem de derivación {ItemId}: Trámites no disponible en el intento {Intentos}; vuelve a llamar desde {DisponibleDesde}.")]
    private static partial void Programado(ILogger logger, Guid itemId, short intentos, DateTimeOffset disponibleDesde);

    [LoggerMessage(EventId = 84321, Level = LogLevel.Information, Message = "Ítem de derivación {ItemId}: ya no está en vuelo; el reintento se descarta.")]
    private static partial void NoProgramado(ILogger logger, Guid itemId);
}
