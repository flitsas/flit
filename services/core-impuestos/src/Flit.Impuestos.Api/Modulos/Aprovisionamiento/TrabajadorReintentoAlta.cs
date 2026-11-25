using Flit.Impuestos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Rondas del aprovisionamiento (plan §2.1, D-ACT): cada <see cref="IntervaloKey"/> segundos (10 por defecto) evalúa con
/// <see cref="Aprovisionador"/> hasta <see cref="LotePendientes"/> compañías en <c>PENDIENTE</c> cuya espera venció (las recién
/// activadas, las reintentadas y los reintentos automáticos de «no salió») y reporta a la plataforma hasta
/// <see cref="LoteReportes"/> filas con una versión sin reportar. La lista de compañías es lo único que se lee entre empresas: solo
/// ids, por SQL parametrizado; cada compañía se evalúa después en su propio contexto con la empresa fijada. Un fallo de una
/// compañía no corta la ronda. Se apaga con <see cref="ActivoKey"/>=false (las pruebas llaman <see cref="EjecutarRondaAsync"/>).
/// </summary>
/// <example>
/// <code>
/// var evaluadas = await trabajador.EjecutarRondaAsync(ct);
/// </code>
/// </example>
public sealed partial class TrabajadorReintentoAlta(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider reloj, ILogger<TrabajadorReintentoAlta> log) : BackgroundService
{
    public const string ActivoKey = "Impuestos:Aprovisionamiento:TrabajadorActivo";
    public const string IntervaloKey = "Impuestos:Aprovisionamiento:IntervaloSegundos";
    public const int IntervaloPorDefectoSegundos = 10;
    public const int LotePendientes = 20;
    public const int LoteReportes = 50;

    /// <summary>Una ronda: evalúa las pendientes vencidas y reporta lo no reportado. Devuelve cuántas evaluó.</summary>
    public async Task<int> EjecutarRondaAsync(CancellationToken ct)
    {
        List<Guid> pendientes;
        List<Guid> sinReportar;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            var ahora = reloj.GetUtcNow();
            pendientes = await db.Database.SqlQuery<Guid>(
                $"SELECT tenant_id AS \"Value\" FROM impuestos.aprovisionamientos WHERE estado = 'PENDIENTE' AND (proximo_intento_en IS NULL OR proximo_intento_en <= {ahora}) ORDER BY proximo_intento_en NULLS FIRST LIMIT {LotePendientes}")
                .ToListAsync(ct).ConfigureAwait(false);
        }

        var evaluadas = 0;
        foreach (var empresa in pendientes)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<Aprovisionador>().EvaluarAsync(empresa, ct).ConfigureAwait(false))
                    evaluadas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                FalloEvaluacion(log, empresa, ex.GetType().Name);
            }
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            sinReportar = await db.Database.SqlQuery<Guid>(
                $"SELECT tenant_id AS \"Value\" FROM impuestos.aprovisionamientos WHERE reportado_version < version ORDER BY actualizado_en LIMIT {LoteReportes}")
                .ToListAsync(ct).ConfigureAwait(false);
        }

        foreach (var empresa in sinReportar)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReportadorEstado>().ReportarAsync(empresa, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                FalloReporte(log, empresa, ex.GetType().Name);
            }
        }

        return evaluadas;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var segundos = configuration.GetValue(IntervaloKey, IntervaloPorDefectoSegundos);
        var intervalo = TimeSpan.FromSeconds(segundos > 0 ? segundos : IntervaloPorDefectoSegundos);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarRondaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                FalloRonda(log, ex.GetType().Name);
            }

            try
            {
                await Task.Delay(intervalo, reloj, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo evaluar el aprovisionamiento de la empresa {Empresa} ({Error}); se reintenta al vencer la reserva.")]
    private static partial void FalloEvaluacion(ILogger logger, Guid empresa, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo reportar el aprovisionamiento de la empresa {Empresa} ({Error}); se reintenta en la siguiente ronda.")]
    private static partial void FalloReporte(ILogger logger, Guid empresa, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La ronda de aprovisionamiento falló ({Error}); se reintenta en el siguiente intervalo.")]
    private static partial void FalloRonda(ILogger logger, string error);
}
