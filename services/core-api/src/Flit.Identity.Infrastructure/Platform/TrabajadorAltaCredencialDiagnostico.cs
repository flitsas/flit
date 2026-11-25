using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Platform;

/// <summary>
/// HU #14711 (AC-DC-03; plan §3.1 punto 1) — rondas del alta automática de la credencial del impuesto de <c>diagnostico</c>, en
/// el host de core-identity. Cada <see cref="IntervaloKey"/> segundos (10 por defecto) reserva con <c>FOR UPDATE SKIP
/// LOCKED</c> hasta <see cref="AltaCredencialDiagnosticoAutomatica.Lote"/> filas en <c>PENDIENTE</c> con el próximo intento
/// vencido (varias réplicas no toman la misma) y atiende cada una en su propio scope con
/// <see cref="AltaCredencialDiagnosticoAutomatica"/>. Un fallo de una empresa no corta la ronda: su fila queda reservada
/// <see cref="AltaCredencialDiagnosticoAutomatica.Reserva"/> y se vuelve a tomar al vencer. Se apaga con
/// <see cref="ActivoKey"/>=false; las pruebas llaman <see cref="EjecutarRondaAsync"/>.
/// </summary>
/// <example>
/// <code>
/// var atendidas = await trabajador.EjecutarRondaAsync(ct);
/// </code>
/// </example>
public sealed partial class TrabajadorAltaCredencialDiagnostico(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider reloj, ILogger<TrabajadorAltaCredencialDiagnostico> log) : BackgroundService
{
    public const string ActivoKey = "Platform:ImpuestoProveedor:TrabajadorActivo";
    public const string IntervaloKey = "Platform:ImpuestoProveedor:IntervaloSegundos";

    private readonly IServiceScopeFactory _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly TimeProvider _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Una ronda; devuelve cuántas empresas atendió sin excepción.</summary>
    public async Task<int> EjecutarRondaAsync(CancellationToken ct)
    {
        IReadOnlyList<PendienteAltaDiagnostico> pendientes;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            pendientes = await scope.ServiceProvider.GetRequiredService<ITenantProductProvisioningStore>()
                .ReservarPendientesAltaDiagnosticoAsync(_reloj.GetUtcNow(), AltaCredencialDiagnosticoAutomatica.Reserva, AltaCredencialDiagnosticoAutomatica.Lote, ct)
                .ConfigureAwait(false);
        }

        var atendidas = 0;
        foreach (var pendiente in pendientes)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AltaCredencialDiagnosticoAutomatica>().ProcesarAsync(pendiente, ct).ConfigureAwait(false);
                atendidas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                FalloEmpresa(_log, pendiente.TenantId, ex.GetType().Name);
            }
        }

        return atendidas;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var segundos = _configuration.GetValue(IntervaloKey, AltaCredencialDiagnosticoAutomatica.IntervaloPorDefectoSegundos);
        var intervalo = TimeSpan.FromSeconds(segundos > 0 ? segundos : AltaCredencialDiagnosticoAutomatica.IntervaloPorDefectoSegundos);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarRondaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                FalloRonda(_log, ex.GetType().Name);
            }

            try
            {
                await Task.Delay(intervalo, _reloj, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo atender el alta de la credencial del impuesto de la empresa {Empresa} ({Error}); se reintenta al vencer la reserva.")]
    private static partial void FalloEmpresa(ILogger logger, Guid empresa, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La ronda del alta de la credencial del impuesto falló ({Error}); se reintenta en el siguiente intervalo.")]
    private static partial void FalloRonda(ILogger logger, string error);
}
